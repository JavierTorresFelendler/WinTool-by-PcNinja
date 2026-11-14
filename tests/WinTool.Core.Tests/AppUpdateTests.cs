using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WinTool.Core.Updates;
using Xunit;

namespace WinTool.Core.Tests
{
    public class AppVersionTests
    {
        [Theory]
        [InlineData("1.0.0", "0.9.9")]
        [InlineData("1.0.0", "1.0.0-rc.3")]
        [InlineData("1.0.0-rc.2", "1.0.0-rc.1")]
        [InlineData("1.0.0-rc.1", "1.0.0-beta.9")]
        [InlineData("1.0.0-beta.1", "1.0.0-alpha.4")]
        [InlineData("1.0.10", "1.0.9")]
        [InlineData("v2.0.0", "1.99.99")]
        public void Newer_versions_compare_higher(string newer, string older)
        {
            Assert.True(AppVersion.Parse(newer).IsNewerThan(AppVersion.Parse(older)));
            Assert.False(AppVersion.Parse(older).IsNewerThan(AppVersion.Parse(newer)));
        }

        [Fact]
        public void Build_metadata_is_ignored_and_file_version_has_four_parts()
        {
            var version = AppVersion.Parse("1.2.3-rc.4+abcdef");
            Assert.Equal("1.2.3-rc.4", version.ToString());
            Assert.Equal(new Version(1, 2, 3, 3004), version.FileVersion);
            Assert.Equal(0, version.CompareTo(AppVersion.Parse("v1.2.3-RC4")));
        }

        [Theory]
        [InlineData("1.0.0-alpha.3", "1.0.0.1003", "1.0.3")]
        [InlineData("1.0.0-beta.2", "1.0.0.2002", "1.0.202")]
        [InlineData("1.0.0-rc.1", "1.0.0.3001", "1.0.401")]
        [InlineData("1.0.0", "1.0.0.9000", "1.0.999")]
        [InlineData("1.0.1", "1.0.1.9000", "1.0.1999")]
        [InlineData("2.3.64-rc.12", "2.3.64.3012", "2.3.64412")]
        public void File_and_msi_versions_follow_D_024(string text, string file, string msi)
        {
            var version = AppVersion.Parse(text);
            Assert.Equal(Version.Parse(file), version.FileVersion);
            Assert.Equal(msi, version.MsiVersion);
        }

        [Fact]
        public void Msi_versions_order_like_releases()
        {
            var ordered = new[] { "1.0.0-alpha.1", "1.0.0-beta.1", "1.0.0-rc.1", "1.0.0-rc.2", "1.0.0", "1.0.1-beta.1", "1.0.1", "1.1.0-rc.1", "1.1.0" }
                .Select(t => Version.Parse(AppVersion.Parse(t).MsiVersion)).ToList();
            for (var i = 1; i < ordered.Count; i++) Assert.True(ordered[i] > ordered[i - 1], ordered[i] + " should be newer than " + ordered[i - 1]);
            Assert.Throws<InvalidOperationException>(() => AppVersion.Parse("1.0.65").MsiVersion);
        }

        [Theory]
        [InlineData("")]
        [InlineData("1.2")]
        [InlineData("1.2.3-preview.1")]
        [InlineData("latest")]
        public void Other_tags_are_not_versions(string text)
        {
            Assert.False(AppVersion.TryParse(text, out _));
        }
    }

    public sealed class ReleaseFeedTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "WinToolUpdates", Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }

        private static string Release(string tag, bool prerelease, bool draft = false, bool complete = true, string host = ProductInfo.RepositoryUrl + "/releases/")
        {
            var assets = new List<string>
            {
                Asset(ReleaseFiles.Executable, host, tag),
                Asset(ReleaseFiles.Installer, host, tag)
            };
            if (complete) assets.Add(Asset(ReleaseFeed.ChecksumsFile, host, tag));
            return "{\"tag_name\":\"" + tag + "\",\"draft\":" + (draft ? "true" : "false") + ",\"prerelease\":" + (prerelease ? "true" : "false") +
                   ",\"body\":\"Notes for " + tag + "\",\"html_url\":\"https://github.com/x/releases/" + tag + "\",\"assets\":[" + string.Join(",", assets) + "]}";
        }

        private static string Asset(string name, string host, string tag) =>
            "{\"name\":\"" + name + "\",\"browser_download_url\":\"" + host + "download/" + tag + "/" + name + "\"}";

        private static string Feed(params string[] releases) => "[" + string.Join(",", releases) + "]";

        [Fact]
        public void Stable_users_get_the_newest_stable_release()
        {
            var feed = ReleaseFeed.Parse(Feed(Release("v1.1.0-rc.1", true), Release("v1.0.1", false), Release("v1.0.0", false)));
            Assert.Equal("1.0.1", ReleaseFeed.Newest(feed, AppVersion.Parse("1.0.0"), false).Version.ToString());
            Assert.Equal("1.1.0-rc.1", ReleaseFeed.Newest(feed, AppVersion.Parse("1.0.0"), true).Version.ToString());
            Assert.Null(ReleaseFeed.Newest(feed, AppVersion.Parse("1.0.1"), false));
        }

        [Fact]
        public void A_tester_on_an_rc_gets_the_final_release()
        {
            var feed = ReleaseFeed.Parse(Feed(Release("v1.0.0", false), Release("v1.0.0-rc.2", true)));
            Assert.Equal("1.0.0", ReleaseFeed.Newest(feed, AppVersion.Parse("1.0.0-rc.2"), false).Version.ToString());
        }

        [Fact]
        public void Drafts_incomplete_mislabelled_and_foreign_releases_are_ignored()
        {
            var feed = ReleaseFeed.Parse(Feed(
                Release("v9.0.0", false, draft: true),
                Release("v8.0.0", false, complete: false),
                Release("v7.0.0-rc.1", false),                 // test version not marked pre-release
                Release("v6.0.0", true),                       // stable version marked pre-release
                Release("v5.0.0", false, host: "https://evil.test/"),
                Release("nightly", false),
                Release("v1.0.0", false)));
            Assert.Equal(new[] { "1.0.0" }, feed.Select(r => r.Version.ToString()));
            Assert.Equal("Notes for v1.0.0", feed[0].Notes);
        }

        [Fact]
        public void A_local_test_feed_accepts_only_files_beside_it()
        {
            Directory.CreateDirectory(folder);
            var feed = Path.Combine(folder, "releases.json");
            var beside = Path.Combine(folder, "WinTool-by-PcNinja.exe");
            var allowed = ReleaseFeed.LocalAssetsBeside(feed);
            Assert.True(allowed(beside));
            Assert.False(allowed(Path.Combine(folder, "sub", "WinTool-by-PcNinja.exe")));
            Assert.False(allowed(Path.Combine(Path.GetTempPath(), "WinTool-by-PcNinja.exe")));
            Assert.False(allowed("https://github.com/x/dl/WinTool-by-PcNinja.exe"));
            Assert.False(ReleaseFeed.IsGitHubAsset(beside));
        }

        [Fact]
        public void Checksums_follow_the_sha256sum_format()
        {
            var hash = new string('a', 64);
            var map = ReleaseFeed.ParseChecksums(hash + "  WinTool-by-PcNinja.exe\r\n" + hash.ToUpperInvariant() + " *WinTool-by-PcNinja.msi\nbad line\n");
            Assert.Equal(hash.ToUpperInvariant(), map["winTool-by-pcninja.EXE"]);
            Assert.True(map.ContainsKey("WinTool-by-PcNinja.msi"));
            Assert.Equal(2, map.Count);
        }

        private sealed class FakeWeb : IUpdateWeb
        {
            public Dictionary<string, byte[]> Files { get; } = new Dictionary<string, byte[]>();

            public Task<string> GetStringAsync(string url, CancellationToken token) => Task.FromResult(Encoding.UTF8.GetString(Files[url]));

            public Task DownloadAsync(string url, string path, IProgress<long> progress, CancellationToken token)
            {
                File.WriteAllBytes(path, Files[url]);
                progress?.Report(Files[url].Length);
                return Task.CompletedTask;
            }
        }

        private (AppUpdater Updater, ReleaseInfo Release, FakeWeb Web) Setup(byte[] exe, string sumsHash, string product = ProductInfo.Name, string fileVersion = "1.2.0.9000")
        {
            var release = ReleaseFeed.Parse(Feed(Release("v1.2.0", false))).Single();
            var web = new FakeWeb();
            web.Files[release.AssetUrl(ReleaseFiles.Executable)] = exe;
            web.Files[release.AssetUrl(ReleaseFeed.ChecksumsFile)] = Encoding.UTF8.GetBytes(sumsHash + "  " + ReleaseFiles.Executable + "\n");
            var updater = new AppUpdater(web, folder, path => new ExeIdentity(product, fileVersion));
            return (updater, release, web);
        }

        private static string Hash(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }

        [Fact]
        public async Task A_matching_download_is_kept()
        {
            var bytes = Encoding.UTF8.GetBytes("new exe");
            var setup = Setup(bytes, Hash(bytes).ToLowerInvariant());
            var path = await setup.Updater.DownloadVerifiedAsync(setup.Release, ReleaseFiles.Executable, null, CancellationToken.None);
            Assert.Equal(bytes, File.ReadAllBytes(path));
            Assert.Equal(ReleaseFiles.Executable, Path.GetFileName(path));
        }

        [Fact]
        public async Task A_wrong_checksum_is_deleted()
        {
            var bytes = Encoding.UTF8.GetBytes("tampered");
            var setup = Setup(bytes, new string('0', 64));
            await Assert.ThrowsAsync<UpdateVerificationException>(() => setup.Updater.DownloadVerifiedAsync(setup.Release, ReleaseFiles.Executable, null, CancellationToken.None));
            Assert.Empty(Directory.GetFiles(folder, "*", SearchOption.AllDirectories));
        }

        [Theory]
        [InlineData("Other Product", "1.2.0.0")]
        [InlineData(ProductInfo.Name, "1.2.0.0")]
        public async Task An_exe_that_is_not_the_release_is_deleted(string product, string version)
        {
            var bytes = Encoding.UTF8.GetBytes("exe");
            var setup = Setup(bytes, Hash(bytes), product, version);
            await Assert.ThrowsAsync<UpdateVerificationException>(() => setup.Updater.DownloadVerifiedAsync(setup.Release, ReleaseFiles.Executable, null, CancellationToken.None));
            Assert.Empty(Directory.GetFiles(folder, "*", SearchOption.AllDirectories));
        }

        [Fact]
        public void The_portable_swap_keeps_the_previous_copy_and_never_overwrites_an_archive()
        {
            Directory.CreateDirectory(folder);
            var running = Path.Combine(folder, ReleaseFiles.Executable);
            var downloaded = Path.Combine(folder, "download.exe");
            File.WriteAllText(running, "old");
            File.WriteAllText(downloaded, "new");
            File.WriteAllText(Path.Combine(folder, "WinTool-by-PcNinja-v1.0.0.0.exe"), "older archive");

            var archive = PortableSwap.Apply(running, downloaded, new Version(1, 0, 0, 0));

            Assert.Equal("WinTool-by-PcNinja-v1.0.0.0-2.exe", Path.GetFileName(archive));
            Assert.Equal("old", File.ReadAllText(archive));
            Assert.Equal("new", File.ReadAllText(running));
            Assert.Equal("older archive", File.ReadAllText(Path.Combine(folder, "WinTool-by-PcNinja-v1.0.0.0.exe")));
        }
    }
}

namespace WinTool.Core.Tests
{
    public class UpdateGateTests
    {
        private sealed class Lease : System.IDisposable
        {
            public bool Disposed { get; private set; }
            public void Dispose() => Disposed = true;
        }

        [Xunit.Fact]
        public void The_update_proceeds_only_with_the_lock_and_nothing_else_running()
        {
            var lease = new Lease();
            Xunit.Assert.Same(lease, WinTool.Core.Updates.UpdateGate.TryEnter(() => lease, () => false));
            Xunit.Assert.False(lease.Disposed);
        }

        [Xunit.Fact]
        public void Another_process_holding_the_lock_stops_the_update()
        {
            Xunit.Assert.Null(WinTool.Core.Updates.UpdateGate.TryEnter(() => null, () => false));
        }

        [Xunit.Fact]
        public void An_operation_that_started_during_the_download_stops_the_update_and_releases_the_lock()
        {
            // Codex's scenario: Office starts while the update downloads.
            var lease = new Lease();
            var running = false;
            System.Action downloadFinishesWhileOfficeStarts = () => running = true;
            downloadFinishesWhileOfficeStarts();
            Xunit.Assert.Null(WinTool.Core.Updates.UpdateGate.TryEnter(() => lease, () => running));
            Xunit.Assert.True(lease.Disposed);
        }
    }
}
