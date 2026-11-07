using System;
using System.IO;
using Xunit;

namespace WinTool.Core.Tests
{
    public class WinToolPathsTests
    {
        private static string Special(Environment.SpecialFolder folder) =>
            folder == Environment.SpecialFolder.CommonApplicationData ? Path.Combine(Root(), "PD") : Path.Combine(Root(), "LAD");

        private static string Root() => Path.GetPathRoot(Path.GetTempPath());

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("relative\\folder")]
        [InlineData("relative")]
        public void Without_an_absolute_root_uses_the_normal_folders(string root)
        {
            var folders = WinToolPaths.Resolve(root, Special);
            Assert.Equal(Path.Combine(Root(), "PD", "PcNinja", "WinTool"), folders.MachineData);
            Assert.Equal(Path.Combine(Root(), "LAD", "PcNinja", "WinTool"), folders.UserData);
        }

        [Fact]
        public void An_absolute_root_holds_machine_and_user_data()
        {
            var root = Path.Combine(Path.GetTempPath(), "wintool-test-root");
            var folders = WinToolPaths.Resolve("  \"" + root + "\" ", Special);
            Assert.Equal(Path.Combine(root, "ProgramData"), folders.MachineData);
            Assert.Equal(Path.Combine(root, "User"), folders.UserData);
        }

        [Fact]
        public void Default_paths_follow_the_machine_and_user_folders()
        {
            Assert.Equal(Path.Combine(WinToolPaths.MachineData, "Logs"), Diagnostics.AppLog.DefaultDirectory);
            Assert.Equal(Path.Combine(WinToolPaths.MachineData, "Updates"), Updates.AppUpdater.DefaultDownloadFolder);
            Assert.Equal(Path.Combine(WinToolPaths.UserData, "settings.json"), Settings.SettingsStore.DefaultPath);
        }
    }
}
