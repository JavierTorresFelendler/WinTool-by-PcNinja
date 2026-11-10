using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace WinTool.Core.Updates
{
    /// <summary>The network side of an update, replaceable in tests.</summary>
    public interface IUpdateWeb
    {
        Task<string> GetStringAsync(string url, CancellationToken token);
        Task DownloadAsync(string url, string path, IProgress<long> progress, CancellationToken token);
    }

    /// <summary>Product name and file version from an EXE's version resource.</summary>
    public sealed class ExeIdentity
    {
        public ExeIdentity(string productName, string fileVersion)
        {
            ProductName = productName;
            FileVersion = fileVersion;
        }

        public string ProductName { get; }
        public string FileVersion { get; }

        public static ExeIdentity Read(string path)
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return new ExeIdentity(info.ProductName, info.FileVersion);
        }
    }

    public sealed class UpdateVerificationException : Exception
    {
        public UpdateVerificationException(string message) : base(message) { }
    }

    /// <summary>
    /// Checks GitHub for a newer WinTool and downloads it verified (D-018, D-023):
    /// the SHA-256 must match the release's SHA256SUMS.txt, and a downloaded EXE
    /// must be WinTool with exactly the release's file version. Releases are
    /// unsigned for now (D-009), so trust rests on the GitHub account.
    /// </summary>
    public sealed class AppUpdater
    {
        private readonly IUpdateWeb web;
        private readonly string downloadFolder;
        private readonly Func<string, ExeIdentity> readVersion;

        public AppUpdater()
            : this(new HttpUpdateWeb(), DefaultDownloadFolder, ExeIdentity.Read)
        {
        }

        public AppUpdater(IUpdateWeb web, string downloadFolder, Func<string, ExeIdentity> readVersion)
        {
            this.web = web;
            this.downloadFolder = downloadFolder;
            this.readVersion = readVersion;
        }

        public const string FeedVariable = "WINTOOL_UPDATE_FEED";

        public static string DefaultDownloadFolder => Path.Combine(WinToolPaths.MachineData, "Updates");

        /// <summary>
        /// Where releases are read from. The environment variable WINTOOL_UPDATE_FEED may point to
        /// another releases JSON (https or a local file) for testing before the repository is public.
        /// </summary>
        public static string FeedUrl
        {
            get
            {
                var custom = Environment.GetEnvironmentVariable(FeedVariable);
                return string.IsNullOrWhiteSpace(custom) ? ReleaseFeed.ApiUrl : custom.Trim();
            }
        }

        /// <summary>github.com assets; for a local test feed file, only files beside it.</summary>
        private static Func<string, bool> AllowedAsset()
        {
            var feed = FeedUrl;
            return File.Exists(feed) ? ReleaseFeed.LocalAssetsBeside(feed) : ReleaseFeed.IsGitHubAsset;
        }

        public async Task<ReleaseInfo> CheckAsync(AppVersion current, bool includeTestVersions, CancellationToken token)
        {
            var json = await web.GetStringAsync(FeedUrl, token).ConfigureAwait(false);
            return ReleaseFeed.Newest(ReleaseFeed.Parse(json, AllowedAsset()), current, includeTestVersions);
        }

        /// <summary>The published release of exactly this version (test versions included), or null.</summary>
        public async Task<ReleaseInfo> FindReleaseAsync(AppVersion version, CancellationToken token)
        {
            var json = await web.GetStringAsync(FeedUrl, token).ConfigureAwait(false);
            foreach (var release in ReleaseFeed.Parse(json, AllowedAsset()))
                if (release.Version.CompareTo(version) == 0) return release;
            return null;
        }

        /// <summary>Downloads one asset of the release and verifies it; returns its path.</summary>
        public async Task<string> DownloadVerifiedAsync(ReleaseInfo release, string assetName, IProgress<long> progress, CancellationToken token)
        {
            var url = release.AssetUrl(assetName) ?? throw new UpdateVerificationException(assetName + " is not part of release " + release.Tag + ".");
            var sums = ReleaseFeed.ParseChecksums(await web.GetStringAsync(release.AssetUrl(ReleaseFeed.ChecksumsFile), token).ConfigureAwait(false));
            if (!sums.TryGetValue(assetName, out var expected))
                throw new UpdateVerificationException(ReleaseFeed.ChecksumsFile + " of " + release.Tag + " has no line for " + assetName + ".");

            var folder = Path.Combine(downloadFolder, release.Version.ToString());
            WinToolPaths.EnsureMachineDataDirectory(folder);
            var path = Path.Combine(folder, assetName);
            var partial = path + ".partial";
            if (File.Exists(partial)) File.Delete(partial);
            await web.DownloadAsync(url, partial, progress, token).ConfigureAwait(false);

            var actual = Sha256(partial);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(partial);
                throw new UpdateVerificationException(assetName + " does not match its SHA-256 (expected " + expected + ", got " + actual + "). It was deleted.");
            }

            if (assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                var info = readVersion(partial);
                var fileVersion = Version.TryParse(info.FileVersion ?? string.Empty, out var parsed) ? parsed : null;
                if (info.ProductName != ProductInfo.Name || fileVersion != release.Version.FileVersion)
                {
                    File.Delete(partial);
                    throw new UpdateVerificationException(assetName + " is not " + ProductInfo.Name + " " + release.Version.FileVersion +
                        " (found " + info.ProductName + " " + info.FileVersion + "). It was deleted.");
                }
            }

            if (File.Exists(path)) File.Delete(path);
            File.Move(partial, path);
            return path;
        }

        public static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                return Compat.ToHexString(sha.ComputeHash(stream));
            }
        }
    }

    public sealed class HttpUpdateWeb : IUpdateWeb
    {
        private static readonly HttpClient Http = Create();

        public async Task<string> GetStringAsync(string url, CancellationToken token)
        {
            if (File.Exists(url)) return File.ReadAllText(url);
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                using (var response = await Http.GetAsync(url, timeout.Token).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
        }

        public async Task DownloadAsync(string url, string path, IProgress<long> progress, CancellationToken token)
        {
            if (File.Exists(url))
            {
                // A local test feed's asset (only accepted beside a local feed file, see ReleaseFeed).
                File.Copy(url, path, false);
                progress?.Report(new FileInfo(path).Length);
                return;
            }

            using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) > 0)
                    {
                        await output.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
                        total += read;
                        progress?.Report(total);
                    }
                }
            }
        }

        private static HttpClient Create()
        {
            var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            // GitHub's API requires a User-Agent.
            http.DefaultRequestHeaders.UserAgent.ParseAdd("WinTool-by-PcNinja/" + ProductInfo.Version);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return http;
        }
    }

    /// <summary>
    /// Replaces a portable WinTool-by-PcNinja.exe with a downloaded one (D-018).
    /// Windows lets a running EXE be renamed, so the running copy becomes
    /// WinTool-by-PcNinja-v&lt;its file version&gt;.exe and the new one takes the official name.
    /// </summary>
    public static class PortableSwap
    {
        /// <summary>Returns the path the previous copy was moved to.</summary>
        public static string Apply(string runningExe, string downloadedExe, Version runningFileVersion)
        {
            var folder = Path.GetDirectoryName(Path.GetFullPath(runningExe));
            var target = Path.Combine(folder, ReleaseFiles.Executable);
            var archive = Path.Combine(folder, ReleaseFiles.PreviousCopyName(runningFileVersion));
            if (File.Exists(archive))
            {
                // Same version archived before (an update was repeated): keep that one, number this one.
                var n = 2;
                while (File.Exists(archive)) archive = Path.Combine(folder, Path.GetFileNameWithoutExtension(ReleaseFiles.PreviousCopyName(runningFileVersion)) + "-" + n++ + ".exe");
            }

            File.Move(runningExe, archive);
            try
            {
                File.Copy(downloadedExe, target, false);
            }
            catch
            {
                // Put the running copy back so the user is never left without WinTool.
                if (!File.Exists(runningExe)) File.Move(archive, runningExe);
                throw;
            }

            return archive;
        }
    }
}
