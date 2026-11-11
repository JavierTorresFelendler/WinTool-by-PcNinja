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
