using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using WinTool.Core.Security;
using WinTool.Core.IO;
using Xunit;

namespace WinTool.Core.Tests
{
    public class ProtectedMachineDataTests
    {
        private sealed class Lease : IDisposable { public void Dispose() { } }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Planted_file_or_junction_is_retained_without_following_and_replaced(bool junction)
        {
            using (var temp = new TempDirectory())
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var acl = new DirectorySecurity(); acl.SetOwner(identity.User); acl.SetAccessRuleProtection(true, false);
                acl.AddAccessRule(new FileSystemAccessRule(identity.User, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                new DirectoryInfo(temp.Path).SetAccessControl(acl);
                var root = temp.File("Machine");
                var target = temp.File("Target"); Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "keep.txt"), "unchanged");
                if (junction)
                {
                    using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + root + "\" \"" + target + "\"")
                        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
                    { process.WaitForExit(); Assert.Equal(0, process.ExitCode); }
                }
                else File.WriteAllText(root, "untrusted");
                try
                {
                    ProtectedMachineData.Ensure(root, root, true, identity.User, () => new Lease());
                    Assert.True(Directory.Exists(root));
                    Assert.False(File.Exists(Path.Combine(root, "keep.txt")));
                    Assert.Equal("unchanged", File.ReadAllText(Path.Combine(target, "keep.txt")));
                    Assert.Single(Directory.GetFileSystemEntries(temp.Path, "Machine.untrusted-*"));
                    ProtectedMachineData.ReleaseForTest(root);
                    ProtectedMachineData.Ensure(root, root, true, identity.User, () => { throw new Exception("Safe uncached validation needs no lock"); });
                }
                finally { ProtectedMachineData.ReleaseForTest(root); }
            }
        }

        [Fact]
        public void Ancestor_delete_is_unsafe_and_installer_grants_are_trusted_only_for_direct_targets()
        {
            var admin = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
            var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            var installer = new SecurityIdentifier("S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464");
            var acl = new DirectorySecurity(); acl.SetOwner(admin); acl.SetAccessRuleProtection(true, false);
            acl.AddAccessRule(new FileSystemAccessRule(users, FileSystemRights.Delete, AccessControlType.Allow));
            Assert.False(ProtectedMachineData.SafeAcl(acl, admin, false, true));
            acl.RemoveAccessRuleAll(new FileSystemAccessRule(users, FileSystemRights.Delete, AccessControlType.Allow));
            acl.AddAccessRule(new FileSystemAccessRule(installer, FileSystemRights.FullControl, AccessControlType.Allow));
            Assert.False(ProtectedMachineData.SafeAcl(acl, admin, false, false));
            Assert.True(ProtectedMachineData.SafeAcl(acl, admin, false, false, true));
        }

        [Fact]
        public void Completed_migration_reads_without_acquiring_a_busy_lock()
        {
            using (var temp = new TempDirectory())
            {
                File.WriteAllText(temp.File("migration-v1.complete.json"), "{\"schemaVersion\":1,\"complete\":true}");
                MachineDataMigration.Run(temp.Path, temp.File("old"), new MigrationRule[0],
                    () => { throw new Exception("Must not acquire"); }, _ => { throw new Exception("Must not prepare"); }, File.ReadAllText, File.WriteAllText);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Validator_conversion_failure_is_rejected_without_blocking_completion(bool overflow)
        {
            using (var temp = new TempDirectory())
            {
                File.WriteAllText(temp.File("state.json"), "{}");
                var destination = temp.File("New");
                MachineDataMigration.Run(destination, temp.Path, new[] { new MigrationRule("state.json", _ =>
                    { if (overflow) throw new OverflowException(); throw new InvalidCastException(); }) }, () => new Lease(),
                    path => Directory.CreateDirectory(path), File.ReadAllText, File.WriteAllText);
                Assert.True(File.Exists(Path.Combine(destination, "migration-v1.complete.json")));
                Assert.False(File.Exists(Path.Combine(destination, "state.json")));
            }
        }

        [Fact]
        public void Fresh_objects_replace_unsafe_tree_and_old_security_handles_do_not_reach_them()
        {
            using (var temp = new TempDirectory())
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var root = temp.File("Machine");
                var parentAcl = new DirectorySecurity();
                parentAcl.SetOwner(identity.User); parentAcl.SetAccessRuleProtection(true, false);
                parentAcl.AddAccessRule(new FileSystemAccessRule(identity.User, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                new DirectoryInfo(temp.Path).SetAccessControl(parentAcl);
                Directory.CreateDirectory(root);
                var bad = new DirectoryInfo(root).GetAccessControl();
                bad.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null), FileSystemRights.Write, AccessControlType.Allow));
                new DirectoryInfo(root).SetAccessControl(bad);
                File.WriteAllText(Path.Combine(root, "old.txt"), "not adopted");
                using (var held = CreateFileW(root, 0x60000, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero))
                {
                    Assert.False(held.IsInvalid);
                    try
                    {
                        ProtectedMachineData.Ensure(root, root, true, identity.User, () => new Lease());
                        ProtectedMachineData.Ensure(root, root, true, identity.User, () => { throw new Exception("Validation needs no lock"); });
                        Assert.False(File.Exists(Path.Combine(root, "old.txt")));
                        Assert.Single(Directory.GetDirectories(temp.Path, "Machine.untrusted-*"));
                        Assert.True(SetKernelObjectSecurity(held, 4, bad.GetSecurityDescriptorBinaryForm()));
                        Assert.True(ProtectedMachineData.SafeAcl(new DirectoryInfo(root).GetAccessControl(), identity.User, true, false));
                    }
                    finally { ProtectedMachineData.ReleaseForTest(root); }
                }
            }
        }

        [Fact]
        public void Read_only_initialization_does_not_create_a_missing_root()
        {
            using (var temp = new TempDirectory())
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var root = temp.File("Missing");
                Assert.Throws<UnauthorizedAccessException>(() => ProtectedMachineData.Ensure(root, root, false, identity.User, () => { throw new Exception("No lock for read-only creation"); }));
                Assert.False(Directory.Exists(root));
            }
        }

        [Fact]
        public void Migration_is_locked_once_reserializes_and_never_imports_payloads_or_logs()
        {
            using (var temp = new TempDirectory())
            {
                var old = temp.File("Old"); var root = temp.File("New"); Directory.CreateDirectory(old);
                File.WriteAllText(Path.Combine(old, "settings.json"), "  input ");
                File.WriteAllText(Path.Combine(old, "payload.exe"), "payload");
                var held = false; var validates = 0;
                Action run = () => MachineDataMigration.Run(root, old, new[] { new MigrationRule("settings.json", input => { Assert.True(held); validates++; return "validated:" + input.Trim(); }) },
                    () => { Assert.False(held); held = true; return new Callback(() => held = false); },
                    path => { Assert.True(held); Directory.CreateDirectory(path); }, File.ReadAllText,
                    (path, text) => { Assert.True(held); File.WriteAllText(path, text); });
                run(); File.WriteAllText(Path.Combine(old, "settings.json"), "changed"); run();
                Assert.Equal(1, validates);
                Assert.Equal("validated:input", File.ReadAllText(Path.Combine(root, "settings.json")));
                Assert.False(File.Exists(Path.Combine(root, "payload.exe")));
                Assert.True(File.Exists(Path.Combine(root, "migration-v1.complete.json")));
            }
        }

        private sealed class Callback : IDisposable
        { private readonly Action action; public Callback(Action action) { this.action = action; } public void Dispose() { action(); } }

        [Fact]
        public void Destination_write_failure_does_not_complete_the_migration()
        {
            using (var temp = new TempDirectory())
            {
                var legacy = temp.File("Old"); Directory.CreateDirectory(legacy);
                File.WriteAllText(Path.Combine(legacy, "settings.json"), "input");
                var root = temp.File("New");
                var rules = new[] { new MigrationRule("settings.json", text => "validated:" + text) };
                Assert.Throws<IOException>(() => MachineDataMigration.Run(root, legacy, rules, () => new Lease(),
                    path => Directory.CreateDirectory(path), File.ReadAllText, (path, text) => { throw new IOException("destination fixture"); }));
                Assert.False(File.Exists(Path.Combine(root, "migration-v1.complete.json")));
                MachineDataMigration.Run(root, legacy, rules, () => new Lease(), path => Directory.CreateDirectory(path), File.ReadAllText, File.WriteAllText);
                Assert.Equal("validated:input", File.ReadAllText(Path.Combine(root, "settings.json")));
                Assert.True(File.Exists(Path.Combine(root, "migration-v1.complete.json")));
            }
        }

        [Fact]
        public void Linked_input_is_rejected_and_failed_completion_can_be_retried()
        {
            using (var temp = new TempDirectory())
            {
                var source = temp.File("settings.json"); var link = temp.File("linked.json"); File.WriteAllText(source, "input");
                Assert.True(CreateHardLinkW(link, source, IntPtr.Zero));
                Assert.Throws<UnauthorizedAccessException>(() => ProtectedMachineData.ReadUntrustedFile(link));
                var root = temp.File("new");
                Assert.Throws<IOException>(() => MachineDataMigration.Run(root, temp.Path, new MigrationRule[0], () => new Lease(),
                    path => Directory.CreateDirectory(path), File.ReadAllText, (path, text) => { if (path.EndsWith("complete.json")) throw new IOException("fixture"); File.WriteAllText(path, text); }));
                Assert.False(File.Exists(Path.Combine(root, "migration-v1.complete.json")));
                MachineDataMigration.Run(root, temp.Path, new MigrationRule[0], () => new Lease(), path => Directory.CreateDirectory(path), File.ReadAllText, File.WriteAllText);
                Assert.True(File.Exists(Path.Combine(root, "migration-v1.complete.json")));
            }
        }

        [Fact]
        public void Reparse_input_is_not_followed()
        {
            using (var temp = new TempDirectory())
            {
                var target = temp.File("Target"); Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "state.json"), "unchanged");
                var link = temp.File("Link");
                using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + link + "\" \"" + target + "\"")
                    { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
                { process.WaitForExit(); Assert.Equal(0, process.ExitCode); }
                Assert.Throws<UnauthorizedAccessException>(() => ProtectedMachineData.ReadUntrustedFile(Path.Combine(link, "state.json")));
                Assert.Equal("unchanged", File.ReadAllText(Path.Combine(target, "state.json")));
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CreateHardLinkW(string path, string existing, IntPtr security);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("advapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetKernelObjectSecurity(SafeFileHandle handle, uint information, byte[] descriptor);
    }
}
