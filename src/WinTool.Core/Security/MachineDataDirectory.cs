using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinTool.Core.Security
{
    /// <summary>Owns only WinTool's machine-data tree, never other PcNinja products.</summary>
    internal static class MachineDataDirectory
    {
        internal static readonly SecurityIdentifier Administrators = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        private static readonly SecurityIdentifier SystemAccount = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        private static readonly SecurityIdentifier Users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        private static readonly object Gate = new object();

        internal static void Ensure(string root, string directory)
        {
            using (var identity = WindowsIdentity.GetCurrent())
                Ensure(root, directory, new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator), Administrators);
        }

        // Tests supply only an isolated temporary root and its current-user owner.
        internal static void Ensure(string root, string directory, bool canRepair, SecurityIdentifier owner)
        {
            lock (Gate)
            {
                var pins = new List<SafeFileHandle>();
                try
                {
                    // Keep each existing ancestor stable while checking child paths. Never follow reparse points.
                    var ancestors = new Stack<DirectoryInfo>();
                    for (var parent = new DirectoryInfo(root).Parent; parent != null; parent = parent.Parent) ancestors.Push(parent);
                    foreach (var ancestor in ancestors)
                        if (ancestor.Exists) pins.Add(Pin(ancestor.FullName, true));
                    RejectReparseAncestors(root);
                    if (!Directory.Exists(root))
                    {
                        if (!canRepair) throw Refused();
                        var parent = Path.GetDirectoryName(root);
                        Directory.CreateDirectory(parent);
                        pins.Add(Pin(parent, true));
                        RejectReparseAncestors(root);
                        CheckParent(root, owner);
                        Create(root, DirectoryAcl(owner));
                    }
                    pins.Add(Pin(root, false));
                    CheckParent(root, owner);
                    ProtectTree(new DirectoryInfo(root), canRepair, owner, true);
                    var relative = Path.GetFullPath(directory).Substring(Path.GetFullPath(root).TrimEnd('\\', '/').Length).TrimStart('\\', '/');
                    var current = root;
                    foreach (var part in relative.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        current = Path.Combine(current, part);
                        if (!Directory.Exists(current))
                        {
                            if (!canRepair) throw Refused();
                            Create(current, DirectoryAcl(owner));
                        }
                        ProtectTree(new DirectoryInfo(current), canRepair, owner);
                    }
                }
                finally
                {
                    for (var i = pins.Count - 1; i >= 0; i--) pins[i].Dispose();
                }
            }
        }

        private static void ProtectTree(FileSystemInfo item, bool canRepair, SecurityIdentifier owner, bool requireProtected = false)
        {
            var directory = item as DirectoryInfo;
            using (var pin = Pin(item.FullName, directory == null))
            {
                PinnedRepairTree repairTree = null;
                try
                {
                    item.Refresh();
                    if ((item.Attributes & FileAttributes.ReparsePoint) != 0) throw Refused();
                    FileSystemSecurity acl = directory != null
                        ? directory.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access)
                        : ((FileInfo)item).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
                    if (!IsSafe(acl, owner, requireProtected))
                    {
                        if (!canRepair) throw Refused();
                        // Acquire the existing subtree before changing inheritable permissions,
                        // so propagation cannot hide a file that still has an existing writer.
                        repairTree = new PinnedRepairTree(item);
                        // Set the owner together with a new, protected DACL; never retain old grants.
                        if (directory != null) directory.SetAccessControl(DirectoryAcl(owner));
                        else ((FileInfo)item).SetAccessControl(FileAcl(owner));
                        acl = directory != null
                            ? directory.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access)
                            : ((FileInfo)item).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
                        if (!IsSafe(acl, owner)) throw Refused();
                    }
                    if (directory != null)
                        foreach (var child in directory.EnumerateFileSystemInfos()) ProtectTree(child, canRepair, owner);
                }
                finally { repairTree?.Dispose(); }
            }
        }

        internal static bool IsSafe(FileSystemSecurity acl, SecurityIdentifier owner, bool requireProtected = true)
        {
            var actualOwner = acl.GetOwner(typeof(SecurityIdentifier));
            if ((!owner.Equals(actualOwner) && !SystemAccount.Equals(actualOwner)) || (requireProtected && !acl.AreAccessRulesProtected)) return false;
            var adminFull = false;
            var systemFull = false;
            var usersRead = false;
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType != AccessControlType.Allow || (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0) return false;
                var sid = (SecurityIdentifier)rule.IdentityReference;
                var full = (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl;
                if (owner.Equals(sid)) adminFull |= full;
                else if (SystemAccount.Equals(sid)) systemFull |= full;
                else if ((rule.FileSystemRights & ~(FileSystemRights.ReadAndExecute | FileSystemRights.Synchronize)) != 0) return false;
                if (Users.Equals(sid) && (rule.FileSystemRights & FileSystemRights.ReadAndExecute) == FileSystemRights.ReadAndExecute) usersRead = true;
            }
            return adminFull && systemFull && usersRead;
        }

        internal static DirectorySecurity DirectoryAcl(SecurityIdentifier owner)
        {
            var acl = new DirectorySecurity();
            Configure(acl, owner, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit);
            return acl;
        }

        private static FileSecurity FileAcl(SecurityIdentifier owner)
        {
            var acl = new FileSecurity();
            Configure(acl, owner, InheritanceFlags.None);
            return acl;
        }

        private static void Configure(FileSystemSecurity acl, SecurityIdentifier owner, InheritanceFlags inheritance)
        {
            acl.SetOwner(owner);
            acl.SetAccessRuleProtection(true, false);
            acl.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(SystemAccount, FileSystemRights.FullControl, inheritance, PropagationFlags.None, AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, inheritance, PropagationFlags.None, AccessControlType.Allow));
        }

        private static void RejectReparseAncestors(string path)
        {
            for (var item = new DirectoryInfo(path); item != null; item = item.Parent)
                if (item.Exists && (item.Attributes & FileAttributes.ReparsePoint) != 0) throw Refused();
        }

        private static void CheckParent(string root, SecurityIdentifier owner)
        {
            if (!Administrators.Equals(owner)) return; // Isolated test policy only.
            var acl = new DirectoryInfo(Path.GetDirectoryName(root)).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            var parentOwner = acl.GetOwner(typeof(SecurityIdentifier));
            if (!Administrators.Equals(parentOwner) && !SystemAccount.Equals(parentOwner)) throw Refused();
            const FileSystemRights replacesChild = FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
                if (rule.AccessControlType == AccessControlType.Allow &&
                    (rule.PropagationFlags & PropagationFlags.InheritOnly) == 0 &&
                    !Administrators.Equals(rule.IdentityReference) && !SystemAccount.Equals(rule.IdentityReference) &&
                    (rule.FileSystemRights & replacesChild) != 0) throw Refused();
        }

        private static void Create(string path, DirectorySecurity acl)
        {
#if NETFRAMEWORK
            Directory.CreateDirectory(path, acl);
#else
            acl.CreateDirectory(path);
#endif
        }

        internal static SafeFileHandle Pin(string path, bool allowExistingWriters)
        {
            // Metadata-only access also permits validation of a safe file currently held by its trusted writer.
            // Omitting FILE_SHARE_DELETE pins path identity. Unsafe objects additionally exclude existing writers.
            var isDirectory = (File.GetAttributes(path) & FileAttributes.Directory) != 0;
            var access = !allowExistingWriters || isDirectory ? 0x20081u : 0x20080u;
            var handle = CreateFileW(path, access, allowExistingWriters ? 3u : 1u, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new IOException("WinTool machine data could not be secured while in use.", new Win32Exception(error));
            }
            if (!GetFileInformationByHandleEx(handle, 9, out var attributes, 8) || (attributes.Attributes & (uint)FileAttributes.ReparsePoint) != 0)
            {
                handle.Dispose();
                throw Refused();
            }
            return handle;
        }

        private sealed class PinnedRepairTree : IDisposable
        {
            private readonly List<SafeFileHandle> handles = new List<SafeFileHandle>();
            internal PinnedRepairTree(FileSystemInfo root)
            {
                try { Add(root); }
                catch { Dispose(); throw; }
            }
            private void Add(FileSystemInfo item)
            {
                handles.Add(Pin(item.FullName, false));
                if (item is DirectoryInfo directory)
                    foreach (var child in directory.EnumerateFileSystemInfos()) Add(child);
            }
            public void Dispose()
            {
                for (var i = handles.Count - 1; i >= 0; i--) handles[i].Dispose();
                handles.Clear();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AttributeTagInfo { public uint Attributes; public uint ReparseTag; }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int informationClass, out AttributeTagInfo information, uint size);

        private static UnauthorizedAccessException Refused() => new UnauthorizedAccessException("WinTool machine data requires a trusted owner and protected permissions. Start the installed application as administrator to initialize or repair its data folder.");
    }
}
