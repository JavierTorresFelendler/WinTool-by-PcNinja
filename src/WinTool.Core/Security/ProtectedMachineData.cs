using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using WinTool.Core.Operations;

namespace WinTool.Core.Security
{
    /// <summary>Fresh protected objects under the D-028 root; never repairs an untrusted object in place.</summary>
    public static class ProtectedMachineData
    {
        private static readonly SecurityIdentifier Admin = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        private static readonly SecurityIdentifier SystemAccount = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        private static readonly SecurityIdentifier Users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        private static readonly SecurityIdentifier TrustedInstaller = new SecurityIdentifier("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, SafeFileHandle> Directories = new Dictionary<string, SafeFileHandle>(StringComparer.OrdinalIgnoreCase);
        public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PcNinja-WinTool");

        public static string EnsureDirectory(string path)
        {
            using (var identity = WindowsIdentity.GetCurrent())
                return Ensure(Root, path, new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator), Admin, () => OperationGate.EnterWindows());
        }

        internal static string Ensure(string root, string path, bool elevated, SecurityIdentifier owner, Func<IDisposable> acquire, bool alreadyLocked = false)
        {
            root = Path.GetFullPath(root).TrimEnd('\\');
            path = Path.GetFullPath(path).TrimEnd('\\');
            if (!Within(root, path)) throw new ArgumentException("Path is outside the protected root.");
            lock (Gate)
            {
                var key = owner.Value + "|" + path;
                if (Directories.ContainsKey(key)) return path;
                var parents = new List<SafeFileHandle>();
                try
                {
                    var ancestor = new DirectoryInfo(Path.GetDirectoryName(root));
                    var chain = new Stack<DirectoryInfo>();
                    for (var current = ancestor; current != null; current = current.Parent) chain.Push(current);
                    foreach (var item in chain)
                    {
                        parents.Add(Open(item.FullName, false, false));
                        // The current-SID fixture endpoint trusts only its explicitly secured
                        // temporary parent. Production always supplies Administrators and
                        // checks the entire chain; direct executable checks do too.
                        if (owner.Equals(Admin) && !SafeAcl(item.GetAccessControl(), owner, false, true)) throw Refused();
                    }
                    // The parent may allow creation, but must not let ordinary users replace children or its ACL.
                    if (!SafeAcl(ancestor.GetAccessControl(), owner, false, true)) throw Refused();
                    IDisposable operation = alreadyLocked ? new BorrowedLease() : null;
                    try
                    {
                        var relative = path.Substring(root.Length).TrimStart('\\');
                        var current = root;
                        var parts = new List<string> { root };
                        foreach (var part in relative.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries)) { current = Path.Combine(current, part); parts.Add(current); }
                        foreach (var directory in parts)
                        {
                            var directoryKey = owner.Value + "|" + directory;
                            if (Directories.ContainsKey(directoryKey)) continue;
                            FileAttributes? attributes;
                            try { attributes = File.GetAttributes(directory); }
                            catch (FileNotFoundException) { attributes = null; }
                            catch (DirectoryNotFoundException) { attributes = null; }
                            if (attributes.HasValue && (attributes.Value & (FileAttributes.Directory | FileAttributes.ReparsePoint)) == FileAttributes.Directory)
                            {
                                using (var probe = Open(directory, false, true))
                                {
                                    var acl = new DirectoryInfo(directory).GetAccessControl();
                                    if (SafeAcl(acl, owner, directory == root, false))
                                    {
                                        // Reopen without delete sharing before publishing the cached identity.
                                        Directories.Add(directoryKey, Open(directory, false, false));
                                        continue;
                                    }
                                }
                            }
                            if (!elevated) throw Refused();
                            if (operation == null)
                            {
                                operation = acquire();
                                if (operation == null) throw new OperationBusyException();
                                // Another process may have initialized it before this lease.
                                return Ensure(root, path, elevated, owner, acquire, alreadyLocked: true);
                            }
                            if (attributes.HasValue && !MoveFileExW(directory, directory + ".untrusted-" + Guid.NewGuid().ToString("N"), 0))
                                throw new IOException("Could not retain the untrusted object separately.", new Win32Exception(Marshal.GetLastWin32Error()));
                            CreateFresh(directory, owner);
                            var held = Open(directory, false, false);
                            if (!SafeAcl(new DirectoryInfo(directory).GetAccessControl(), owner, true, false)) { held.Dispose(); throw Refused(); }
                            Directories.Add(directoryKey, held);
                        }
                    }
                    finally { operation?.Dispose(); }
                    return path;
                }
                finally { foreach (var held in parents) held.Dispose(); }
            }
        }

        private sealed class BorrowedLease : IDisposable { public void Dispose() { } }

        internal static bool SafeAcl(FileSystemSecurity acl, SecurityIdentifier owner, bool requireProtected, bool parent, bool installer = false)
        {
            var actual = acl.GetOwner(typeof(SecurityIdentifier));
            if (!owner.Equals(actual) && !SystemAccount.Equals(actual) && !((parent || installer) && (Admin.Equals(actual) || TrustedInstaller.Equals(actual)))) return false;
            if (requireProtected && !acl.AreAccessRulesProtected) return false;
            const FileSystemRights changes = FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
            foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            {
                if (rule.AccessControlType != AccessControlType.Allow || (rule.PropagationFlags & PropagationFlags.InheritOnly) != 0) continue;
                if (owner.Equals(rule.IdentityReference) || SystemAccount.Equals(rule.IdentityReference) || ((parent || installer) && (Admin.Equals(rule.IdentityReference) || TrustedInstaller.Equals(rule.IdentityReference)))) continue;
                var forbidden = parent ? FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership : changes;
                if ((rule.FileSystemRights & forbidden) != 0) return false;
            }
            return true;
        }

        private static void CreateFresh(string path, SecurityIdentifier owner)
        {
            var acl = new DirectorySecurity();
            acl.SetOwner(owner);
            acl.SetAccessRuleProtection(true, false);
            var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            acl.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(SystemAccount, FileSystemRights.FullControl, inherit, PropagationFlags.None, AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, inherit, PropagationFlags.None, AccessControlType.Allow));
            var descriptor = acl.GetSecurityDescriptorBinaryForm();
            var pin = GCHandle.Alloc(descriptor, GCHandleType.Pinned);
            try
            {
                var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = pin.AddrOfPinnedObject() };
                // Unlike Directory.CreateDirectory, ERROR_ALREADY_EXISTS is a failure, never adoption.
                if (!CreateDirectoryW(path, ref security)) throw new IOException("Could not create fresh protected machine data.", new Win32Exception(Marshal.GetLastWin32Error()));
            }
            finally { pin.Free(); }
        }

        public static string ReadUntrustedFile(string path)
        {
            var parents = new List<SafeFileHandle>();
            try
            {
                var chain = new Stack<DirectoryInfo>();
                for (var item = new FileInfo(path).Directory; item != null; item = item.Parent) chain.Push(item);
                foreach (var item in chain) parents.Add(Open(item.FullName, false, false));
                using (var handle = Open(path, true, false))
                using (var stream = new FileStream(handle, FileAccess.Read))
                {
                    if (stream.Length > 1024 * 1024) throw new IOException("Legacy input exceeds the migration size limit.");
                    using (var reader = new StreamReader(stream, new System.Text.UTF8Encoding(false, true))) return reader.ReadToEnd();
                }
            }
            finally { foreach (var held in parents) held.Dispose(); }
        }

        public static FileStream OpenLog(string path)
        {
            var parents = new List<SafeFileHandle>();
            try
            {
                var chain = new Stack<DirectoryInfo>();
                for (var item = new FileInfo(path).Directory; item != null; item = item.Parent) chain.Push(item);
                foreach (var item in chain) parents.Add(Open(item.FullName, false, false));
                return new FileStream(Open(path, true, true), FileAccess.Read);
            }
            finally { foreach (var held in parents) held.Dispose(); }
        }

        public static string[] PlainFiles(string directory)
        {
            var handles = new List<SafeFileHandle>();
            try
            {
                var chain = new Stack<DirectoryInfo>();
                for (var item = new DirectoryInfo(directory); item != null; item = item.Parent) chain.Push(item);
                foreach (var item in chain) handles.Add(Open(item.FullName, false, false));
                return Directory.GetFiles(directory);
            }
            finally { foreach (var held in handles) held.Dispose(); }
        }

        public static bool IsProtectedExecutable(string path)
        {
            var handles = new List<SafeFileHandle>();
            try
            {
                var chain = new Stack<DirectoryInfo>();
                for (var item = new FileInfo(path).Directory; item != null; item = item.Parent) chain.Push(item);
                foreach (var item in chain)
                {
                    handles.Add(Open(item.FullName, false, false));
                    if (!SafeAcl(item.GetAccessControl(), Admin, false, true)) return false;
                }
                using (var held = Open(path, true, false))
                    return SafeAcl(new FileInfo(path).GetAccessControl(), Admin, false, false, true) &&
                        SafeAcl(new FileInfo(path).Directory.GetAccessControl(), Admin, false, false, true);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is NotSupportedException) { return false; }
            finally { foreach (var held in handles) held.Dispose(); }
        }

        public static bool IsMachinePath(string path) => Within(Path.GetFullPath(Root), Path.GetFullPath(path));

        /// <summary>Creates an administrator-owned file atomically; an existing append target must already be protected.</summary>
        public static FileStream OpenWriter(string path, bool append)
        {
            EnsureDirectory(Path.GetDirectoryName(path));
            var acl = new FileSecurity();
            acl.SetOwner(Admin);
            acl.SetAccessRuleProtection(true, false);
            acl.AddAccessRule(new FileSystemAccessRule(Admin, FileSystemRights.FullControl, AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(SystemAccount, FileSystemRights.FullControl, AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(Users, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
            var descriptor = acl.GetSecurityDescriptorBinaryForm();
            var pin = GCHandle.Alloc(descriptor, GCHandleType.Pinned);
            SafeFileHandle handle;
            try
            {
                var security = new SecurityAttributes { Length = Marshal.SizeOf<SecurityAttributes>(), Descriptor = pin.AddrOfPinnedObject() };
                handle = CreateSecureFileW(path, 0x40020080, 1, ref security, 1, 0x00200000, IntPtr.Zero);
            }
            finally { pin.Free(); }
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error(); handle.Dispose();
                if (!append || (error != 80 && error != 183)) throw new IOException("Could not create protected data.", new Win32Exception(error));
                handle = CreateFileW(path, 0x40020080, 1, IntPtr.Zero, 3, 0x00200000, IntPtr.Zero);
            }
            if (handle.IsInvalid) { handle.Dispose(); throw new IOException("Could not open protected data for writing."); }
            if (!GetFileInformationByHandle(handle, out var info) || info.Links != 1 || (info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 ||
                !SafeAcl(new FileInfo(path).GetAccessControl(), Admin, false, false)) { handle.Dispose(); throw Refused(); }
            var stream = new FileStream(handle, FileAccess.Write);
            if (append) stream.Seek(0, SeekOrigin.End);
            return stream;
        }

        public static string ReadProtectedFile(string path)
        {
            if (!IsProtectedExecutable(path)) throw Refused();
            return ReadUntrustedFile(path);
        }

        private static SafeFileHandle Open(string path, bool file, bool allowDelete)
        {
            var handle = CreateFileW(path, 0x20081, allowDelete ? 7u : (file ? 1u : 3u), IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
            if (handle.IsInvalid) { var error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new IOException("Protected data access failed.", new Win32Exception(error)); }
            if (!GetFileInformationByHandle(handle, out var info) || (info.Attributes & (uint)FileAttributes.ReparsePoint) != 0 ||
                (file && (info.Links != 1 || (info.Attributes & (uint)FileAttributes.Directory) != 0)))
            { handle.Dispose(); throw Refused(); }
            return handle;
        }

        private static bool Within(string root, string path) => string.Equals(root, path, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)] private static extern bool MoveFileExW(string source, string target, uint flags);
        private static UnauthorizedAccessException Refused() => new UnauthorizedAccessException("Protected WinTool data is unavailable; administrator initialization is required.");

        internal static void ReleaseForTest(string root)
        {
            lock (Gate)
            {
                var remove = new List<string>();
                foreach (var item in Directories) if (item.Key.IndexOf("|" + root, StringComparison.OrdinalIgnoreCase) >= 0) { item.Value.Dispose(); remove.Add(item.Key); }
                foreach (var key in remove) Directories.Remove(key);
            }
        }

        [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public IntPtr Descriptor; [MarshalAs(UnmanagedType.Bool)] public bool Inherit; }
        [StructLayout(LayoutKind.Sequential)] private struct FileInfoNative
        {
            public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
            public uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] private static extern SafeFileHandle CreateSecureFileW(string path, uint access, uint share, ref SecurityAttributes security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateDirectoryW(string path, ref SecurityAttributes security);
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfoNative information);
    }
}
