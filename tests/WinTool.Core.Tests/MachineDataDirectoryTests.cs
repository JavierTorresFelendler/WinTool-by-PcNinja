using System;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using WinTool.Core.Security;
using Xunit;

namespace WinTool.Core.Tests
{
    public class MachineDataDirectoryTests
    {
        [Fact]
        public void Permissive_existing_tree_is_repaired_including_explicit_child_grants()
        {
            using (var temp = new TempDirectory())
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var owner = identity.User;
                var root = temp.File("Machine");
                var child = Path.Combine(root, "Updates", "1.0.3");
                Directory.CreateDirectory(child);
                var file = Path.Combine(child, "candidate.msi");
                File.WriteAllText(file, "fixture");
                var permissive = new DirectoryInfo(child).GetAccessControl();
                permissive.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.FullControl, AccessControlType.Allow));
                new DirectoryInfo(child).SetAccessControl(permissive);

                MachineDataDirectory.Ensure(root, child, true, owner);

                Assert.True(MachineDataDirectory.IsSafe(new DirectoryInfo(root).GetAccessControl(), owner));
                Assert.True(MachineDataDirectory.IsSafe(new DirectoryInfo(child).GetAccessControl(), owner));
                Assert.True(MachineDataDirectory.IsSafe(new FileInfo(file).GetAccessControl(), owner));
                Assert.Equal("fixture", File.ReadAllText(file));
            }
        }

        [Fact]
        public void Non_elevated_policy_refuses_existing_permissive_folder_without_changing_it()
        {
            using (var temp = new TempDirectory())
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var before = new DirectoryInfo(temp.Path).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
                Assert.Throws<UnauthorizedAccessException>(() => MachineDataDirectory.Ensure(temp.Path, temp.Path, false, identity.User));
                Assert.Equal(before, new DirectoryInfo(temp.Path).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All));
            }
        }

        [Fact]
        public void New_tree_has_protected_permissions_and_can_be_validated_without_repair()
        {
            using (var temp = new TempDirectory())
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var root = temp.File("Machine");
                var child = Path.Combine(root, "Logs");
                MachineDataDirectory.Ensure(root, child, true, identity.User);
                MachineDataDirectory.Ensure(root, child, false, identity.User);
                Assert.True(MachineDataDirectory.IsSafe(new DirectoryInfo(child).GetAccessControl(), identity.User));
            }
        }

        [Fact]
        public void Untrusted_owner_or_additional_writer_is_not_accepted()
        {
            var owner = MachineDataDirectory.Administrators;
            var acl = MachineDataDirectory.DirectoryAcl(owner);
            Assert.True(MachineDataDirectory.IsSafe(acl, owner));
            acl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), FileSystemRights.Write, AccessControlType.Allow));
            Assert.False(MachineDataDirectory.IsSafe(acl, owner));
            acl = MachineDataDirectory.DirectoryAcl(owner);
            acl.SetOwner(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null));
            Assert.False(MachineDataDirectory.IsSafe(acl, owner));
        }
    }
}
