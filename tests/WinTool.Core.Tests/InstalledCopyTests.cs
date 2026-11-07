using System;
using System.IO;
using WinTool.Core.Updates;
using Xunit;

namespace WinTool.Core.Tests
{
    public class InstalledCopyTests
    {
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
