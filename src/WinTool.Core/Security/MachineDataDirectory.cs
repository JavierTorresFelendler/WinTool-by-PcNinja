using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

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
                RejectReparseAncestors(root);
                if (!Directory.Exists(root))
                {
                    if (!canRepair) throw Refused();
                    var parent = Path.GetDirectoryName(root);
                    Directory.CreateDirectory(parent);
                    RejectReparseAncestors(root);
                    CheckParent(root, owner);
                    Create(root, DirectoryAcl(owner));
                }
                CheckParent(root, owner);
                ProtectTree(new DirectoryInfo(root), canRepair, owner);
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
        }

        private static void ProtectTree(FileSystemInfo item, bool canRepair, SecurityIdentifier owner)
        {
            item.Refresh();
            if ((item.Attributes & FileAttributes.ReparsePoint) != 0) throw Refused();
            var directory = item as DirectoryInfo;
            FileSystemSecurity acl = directory != null
                ? directory.GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access)
                : ((FileInfo)item).GetAccessControl(AccessControlSections.Owner | AccessControlSections.Access);
            if (!IsSafe(acl, owner))
            {
                if (!canRepair) throw Refused();
                // Set the owner together with a new, protected DACL; never retain explicit old grants.
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

        internal static bool IsSafe(FileSystemSecurity acl, SecurityIdentifier owner)
        {
            var actualOwner = acl.GetOwner(typeof(SecurityIdentifier));
            if ((!owner.Equals(actualOwner) && !SystemAccount.Equals(actualOwner)) || !acl.AreAccessRulesProtected) return false;
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

        private static UnauthorizedAccessException Refused() => new UnauthorizedAccessException("WinTool machine data requires a trusted owner and protected permissions. Start the installed application as administrator to initialize or repair its data folder.");
    }
}
