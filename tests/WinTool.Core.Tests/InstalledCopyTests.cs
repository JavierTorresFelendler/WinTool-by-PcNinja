using System;
using System.IO;
using WinTool.Core.Updates;
using Xunit;

namespace WinTool.Core.Tests
{
    public class InstalledCopyTests
    {
        [Theory]
        [InlineData(@"C:\Program Files\PcNinja\WinTool\WinTool-by-PcNinja.exe", @"C:\Program Files\PcNinja\WinTool", true)]
        [InlineData(@"C:\PROGRAM FILES\PCNINJA\WINTOOL\WINTOOL-BY-PCNINJA.EXE", @"C:\Program Files\PcNinja\WinTool", true)]
        [InlineData(@"C:\Program Files (x86)\PcNinja\WinTool\WinTool-by-PcNinja.exe", @"C:\Program Files\PcNinja\WinTool", false)]
        [InlineData(@"C:\Program Files Evil\PcNinja\WinTool\WinTool-by-PcNinja.exe", @"C:\Program Files\PcNinja\WinTool", false)]
        [InlineData(@"D:\Tools\Custom\WinTool-by-PcNinja.exe", @"D:\Tools\Custom\", true)]
        [InlineData(@"D:\Tools\Custom Other\WinTool-by-PcNinja.exe", @"D:\Tools\Custom", false)]
        [InlineData(@"D:\Tools\Custom\other.exe", @"D:\Tools\Custom", false)]
        [InlineData(@"D:\Tools\Custom\WinTool-by-PcNinja.exe", null, false)]
        public void Installed_identity_uses_the_exact_registered_executable(string exe, string folder, bool expected) =>
            Assert.Equal(expected, InstalledCopy.IsInstalled(exe, folder));

        private static readonly string Downloaded = Path.Combine(Path.GetTempPath(), "Downloads", "WinTool-by-PcNinja.exe");
        private static readonly string Installed = Path.Combine(Path.GetTempPath(), "PcNinja", "WinTool", "WinTool-by-PcNinja.exe");
        private static readonly Version Alpha2 = new Version(1, 0, 0, 1002);
        private static readonly Version Stable = new Version(1, 0, 0, 9000);

        [Fact]
        public void Nothing_installed_asks() =>
            Assert.Equal(LaunchChoice.Ask, InstalledCopy.Decide(Downloaded, null, null, Stable));

        [Fact]
        public void The_same_version_installed_is_started_instead_of_reinstalled() =>
            Assert.Equal(LaunchChoice.RunInstalled, InstalledCopy.Decide(Downloaded, Installed, Stable, Stable));

        [Fact]
        public void A_newer_installed_version_is_started() =>
            Assert.Equal(LaunchChoice.RunInstalled, InstalledCopy.Decide(Downloaded, Installed, Stable, Alpha2));

        [Fact]
        public void An_older_installed_version_asks_so_installing_updates_it() =>
            Assert.Equal(LaunchChoice.Ask, InstalledCopy.Decide(Downloaded, Installed, Alpha2, Stable));

        [Fact]
        public void The_installed_copy_itself_runs_normally() =>
            Assert.Equal(LaunchChoice.RunThis, InstalledCopy.Decide(Installed, Installed, Stable, Stable));
    }
}
