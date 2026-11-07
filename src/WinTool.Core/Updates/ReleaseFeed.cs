using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WinTool.Core.Json;

namespace WinTool.Core.Updates
{
    public sealed class ReleaseInfo
    {
        public ReleaseInfo(AppVersion version, string tag, string notes, string pageUrl, IReadOnlyDictionary<string, string> assets)
        {
            Version = version;
            Tag = tag;
            Notes = notes ?? string.Empty;
            PageUrl = pageUrl;
            Assets = assets;
        }

        public AppVersion Version { get; }
        public string Tag { get; }
        public string Notes { get; }
        public string PageUrl { get; }

        /// <summary>Asset file name → download URL.</summary>
        public IReadOnlyDictionary<string, string> Assets { get; }

        public string AssetUrl(string name)
        {
            string url;
            return Assets.TryGetValue(name, out url) ? url : null;
        }
    }

    /// <summary>
    /// WinTool releases from the GitHub releases API (D-023). A release counts
    /// only when it is published, its tag is a WinTool version, and it has the
    /// EXE, the MSI and SHA256SUMS.txt.
    /// </summary>
    public static class ReleaseFeed
    {
        public const string ChecksumsFile = "SHA256SUMS.txt";

        public static string ApiUrl => "https://api.github.com/repos/" +
            ProductInfo.RepositoryUrl.Substring("https://github.com/".Length) + "/releases?per_page=30";

        public static IReadOnlyList<ReleaseInfo> Parse(string json) => Parse(json, IsGitHubAsset);

        /// <summary>Assets of published releases come from github.com only.</summary>
        public static bool IsGitHubAsset(string url) => url.StartsWith("https://github.com/", StringComparison.Ordinal);

        /// <summary>
        /// For a local test feed (WINTOOL_UPDATE_FEED set to a file): assets must be files in that
        /// feed's own folder, so the whole lifecycle can be tested without publishing a release.
        /// </summary>
        public static Func<string, bool> LocalAssetsBeside(string feedFile)
        {
            var folder = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(feedFile));
            return url =>
            {
                try
                {
                    var full = System.IO.Path.GetFullPath(url);
                    return string.Equals(System.IO.Path.GetDirectoryName(full), folder, StringComparison.OrdinalIgnoreCase);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is System.IO.PathTooLongException)
                {
                    return false;
                }
            };
        }

        public static IReadOnlyList<ReleaseInfo> Parse(string json, Func<string, bool> allowedAsset)
        {
            var list = JsonData.Parse(json) as List<object>;
            var releases = new List<ReleaseInfo>();
            if (list == null) return releases;
            foreach (var item in list.OfType<Dictionary<string, object>>())
            {
                if (Bool(item, "draft")) continue;
                var tag = Text(item, "tag_name");
                if (!AppVersion.TryParse(tag, out var version)) continue;
                // A test version must be marked pre-release on GitHub and vice versa, so a mistake never reaches stable users.
                if (version.IsTest != Bool(item, "prerelease")) continue;
                var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (item.TryGetValue("assets", out var raw) && raw is List<object> assetList)
                    foreach (var asset in assetList.OfType<Dictionary<string, object>>())
                    {
                        var name = Text(asset, "name");
                        var url = Text(asset, "browser_download_url");
                        if (name != null && url != null && allowedAsset(url)) assets[name] = url;
                    }

                if (!assets.ContainsKey(ReleaseFiles.Executable) || !assets.ContainsKey(ReleaseFiles.Installer) || !assets.ContainsKey(ChecksumsFile))
                    continue;
                releases.Add(new ReleaseInfo(version, tag, Text(item, "body"), Text(item, "html_url"), assets));
            }

            return releases;
        }

        /// <summary>The newest release newer than <paramref name="current"/>; test versions only when asked.</summary>
        public static ReleaseInfo Newest(IEnumerable<ReleaseInfo> releases, AppVersion current, bool includeTestVersions) =>
            releases.Where(r => includeTestVersions || !r.Version.IsTest)
                .Where(r => r.Version.IsNewerThan(current))
                .OrderByDescending(r => r.Version)
                .FirstOrDefault();

        /// <summary>Reads "hash  name" lines (sha256sum format); names are case-insensitive.</summary>
        public static IReadOnlyDictionary<string, string> ParseChecksums(string text)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in (text ?? string.Empty).Split('\n'))
            {
                var parts = line.Trim().Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2 || parts[0].Length != 64 || !parts[0].All(Uri.IsHexDigit)) continue;
                map[parts[1].Trim().TrimStart('*')] = parts[0].ToUpperInvariant();
            }

            return map;
        }

        private static string Text(Dictionary<string, object> map, string key) =>
            map.TryGetValue(key, out var value) ? value as string : null;

        private static bool Bool(Dictionary<string, object> map, string key) =>
            map.TryGetValue(key, out var value) && value is bool flag && flag;
    }
}
