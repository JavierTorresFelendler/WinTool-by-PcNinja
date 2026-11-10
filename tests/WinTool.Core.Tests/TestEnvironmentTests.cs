using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinTool.Core.Json;
using WinTool.Core.Updates;
using Xunit;

namespace WinTool.Core.Tests
{
    [CollectionDefinition("Test environment", DisableParallelization = true)]
    public sealed class TestEnvironmentCollection { }

    [Collection("Test environment")]
    public class TestEnvironmentTests
    {
        [Theory]
        [InlineData("1.0.2", false, false)]
        [InlineData("1.0.2", true, true)]
        [InlineData("1.0.3-alpha.1", false, true)]
        [InlineData("1.0.3-beta.1", false, true)]
        [InlineData("1.0.3-rc.1", false, true)]
        public void Both_overrides_follow_the_build_and_version_policy(string version, bool debug, bool allowed)
        {
            foreach (var value in new[] { @"C:\IsolatedData", @"C:\IsolatedFeed\releases.json" })
                Assert.Equal(allowed ? value : null, TestEnvironment.Resolve(value, debug, AppVersion.Parse(version)));
        }

        [Fact]
        public void Production_consumers_use_the_same_policy()
        {
            using (var temp = new TempDirectory())
            using (new EnvironmentScope(temp.Path, temp.File("feed.json")))
            {
                Assert.Equal(TestEnvironment.Allowed ? temp.Path : null, WinToolPaths.DataRootOverride);
                Assert.Equal(TestEnvironment.Allowed ? temp.File("feed.json") : ReleaseFeed.ApiUrl, AppUpdater.FeedUrl);
                var expected = WinToolPaths.Resolve(TestEnvironment.Allowed ? temp.Path : null, Environment.GetFolderPath);
                Assert.Equal(expected.MachineData, WinToolPaths.MachineData);
                Assert.Equal(expected.UserData, WinToolPaths.UserData);
            }
        }

        [Theory]
        [InlineData(ProductInfo.RepositoryUrl + "/releases/download/v1.0.2/WinTool-by-PcNinja.exe", true)]
        [InlineData("https://github.com/other/repo/releases/download/v1.0.2/WinTool-by-PcNinja.exe", false)]
        [InlineData(ProductInfo.RepositoryUrl + "-other/releases/download/v1.0.2/file.exe", false)]
        [InlineData(ProductInfo.RepositoryUrl + "/blob/main/file.exe", false)]
        [InlineData("http://github.com/JavierTorresFelendler/WinTool-by-PcNinja/releases/download/v1.0.2/file.exe", false)]
        [InlineData("https://github.com.example/JavierTorresFelendler/WinTool-by-PcNinja/releases/download/v1.0.2/file.exe", false)]
        [InlineData(ProductInfo.RepositoryUrl + "/releases/download/v1.0.2/file.exe?other=1", false)]
        [InlineData(null, false)]
        public void Only_repository_release_downloads_are_allowed(string url, bool expected) => Assert.Equal(expected, ReleaseFeed.IsGitHubAsset(url));
    }

    internal sealed class EnvironmentScope : IDisposable
    {
        private readonly string root = Environment.GetEnvironmentVariable(WinToolPaths.DataRootVariable);
        private readonly string feed = Environment.GetEnvironmentVariable(AppUpdater.FeedVariable);
        internal EnvironmentScope(string root, string feed)
        {
            Environment.SetEnvironmentVariable(WinToolPaths.DataRootVariable, root);
            Environment.SetEnvironmentVariable(AppUpdater.FeedVariable, feed);
        }
        public void Dispose()
        {
            Environment.SetEnvironmentVariable(WinToolPaths.DataRootVariable, root);
            Environment.SetEnvironmentVariable(AppUpdater.FeedVariable, feed);
        }
    }

    [Collection("Test environment")]
    public class LocalFeedLifecycleTests
    {
        [Fact]
        public async Task File_only_lifecycle_runs_only_when_test_overrides_are_allowed()
        {
            if (!TestEnvironment.Allowed)
            {
                Assert.False(AppVersion.Current.IsTest);
                return; // The separate RC CI step exercises the complete file-only lifecycle.
            }
            using (var temp = new TempDirectory())
            {
                var exe = temp.File(ReleaseFiles.Executable);
                var msi = temp.File(ReleaseFiles.Installer);
                var sums = temp.File(ReleaseFeed.ChecksumsFile);
                File.WriteAllText(exe, "candidate EXE bytes; never executed");
                File.WriteAllText(msi, "candidate MSI bytes; never installed");
                File.WriteAllText(sums, AppUpdater.Sha256(exe) + "  " + ReleaseFiles.Executable + "\n" + AppUpdater.Sha256(msi) + "  " + ReleaseFiles.Installer);
                var feed = temp.File("releases.json");
                File.WriteAllText(feed, JsonText.Serialize(new[] { new
                {
                    tag_name = "v1.2.0", prerelease = false, draft = false,
                    assets = new[] { exe, msi, sums }.Select(path => new { name = Path.GetFileName(path), browser_download_url = path }).ToArray()
                } }));
                using (new EnvironmentScope(temp.File("Data"), feed))
                {
                    var version = AppVersion.Parse("1.2.0");
                    var updater = new AppUpdater(new HttpUpdateWeb(), AppUpdater.DefaultDownloadFolder, _ => new ExeIdentity(ProductInfo.Name, version.FileVersion.ToString()));
                    var release = await updater.CheckAsync(AppVersion.Parse("1.1.0-rc.1"), false, CancellationToken.None);
                    Assert.NotNull(release);
                    var downloaded = await updater.DownloadVerifiedAsync(release, ReleaseFiles.Executable, null, CancellationToken.None);
                    var installer = await updater.DownloadVerifiedAsync(release, ReleaseFiles.Installer, null, CancellationToken.None);
                    Assert.Equal(File.ReadAllText(msi), File.ReadAllText(installer));
                    var portable = temp.File("Portable");
                    Directory.CreateDirectory(portable);
                    var old = Path.Combine(portable, ReleaseFiles.Executable);
                    File.WriteAllText(old, "previous bytes");
                    var archived = PortableSwap.Apply(old, downloaded, AppVersion.Parse("1.1.0-rc.1").FileVersion);
                    Assert.Equal("previous bytes", File.ReadAllText(archived));
                    Assert.Equal(File.ReadAllText(downloaded), File.ReadAllText(old));
                }
            }
        }
    }
}
