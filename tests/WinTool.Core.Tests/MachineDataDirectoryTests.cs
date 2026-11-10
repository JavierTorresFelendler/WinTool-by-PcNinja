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
                Assert.True(MachineDataDirectory.IsSafe(new DirectoryInfo(child).GetAccessControl(), owner, false));
                Assert.True(MachineDataDirectory.IsSafe(new FileInfo(file).GetAccessControl(), owner, false));
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
        public void A_pinned_directory_cannot_be_renamed_until_validation_releases_it()
        {
            using (var temp = new TempDirectory())
            {
                var root = temp.File("Machine");
                var moved = temp.File("Moved");
                Directory.CreateDirectory(root);
                using (var pin = MachineDataDirectory.Pin(root, false))
                    Assert.Throws<IOException>(() => Directory.Move(root, moved));
                Directory.Move(root, moved);
                Assert.True(Directory.Exists(moved));
            }
        }

        [Fact]
        public void A_safe_file_held_by_a_trusted_writer_can_still_be_validated()
        {
            using (var temp = new TempDirectory())
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var root = temp.File("Machine");
                MachineDataDirectory.Ensure(root, root, true, identity.User);
                var file = Path.Combine(root, "Operation.lock");
                File.WriteAllText(file, "fixture");
                // Elevated runners may default new files to the Administrators owner, unlike
                // a normal developer token. Establish this test policy's trusted owner explicitly.
                var acl = new FileInfo(file).GetAccessControl();
                acl.SetOwner(identity.User);
                new FileInfo(file).SetAccessControl(acl);
                Assert.True(MachineDataDirectory.IsSafe(new FileInfo(file).GetAccessControl(), identity.User, false));
                using (var held = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    MachineDataDirectory.Ensure(root, root, true, identity.User);
            }
        }

        [Fact]
        public void Repair_refuses_an_existing_writer_before_changing_inheritable_permissions()
        {
            using (var temp = new TempDirectory())
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var root = temp.File("Machine");
                Directory.CreateDirectory(root);
                var file = Path.Combine(root, "Settings.json");
                using (var held = new FileStream(file, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                    Assert.Throws<IOException>(() => MachineDataDirectory.Ensure(root, root, true, identity.User));
                    Assert.False(new DirectoryInfo(root).GetAccessControl().AreAccessRulesProtected);
                }
                MachineDataDirectory.Ensure(root, root, true, identity.User);
                Assert.True(MachineDataDirectory.IsSafe(new DirectoryInfo(root).GetAccessControl(), identity.User));
            }
        }

        [Fact]
        public void A_junction_is_refused_without_changing_its_target()
        {
            using (var temp = new TempDirectory())
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var root = temp.File("Machine");
                var target = temp.File("Target");
                Directory.CreateDirectory(root);
                Directory.CreateDirectory(target);
                var before = new DirectoryInfo(target).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All);
                var start = new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + Path.Combine(root, "Link") + "\" \"" + target + "\"")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                using (var process = System.Diagnostics.Process.Start(start))
                {
                    process.WaitForExit();
                    Assert.Equal(0, process.ExitCode);
                }
                Assert.Throws<UnauthorizedAccessException>(() => MachineDataDirectory.Ensure(root, root, true, identity.User));
                Assert.Equal(before, new DirectoryInfo(target).GetAccessControl().GetSecurityDescriptorSddlForm(AccessControlSections.All));
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
