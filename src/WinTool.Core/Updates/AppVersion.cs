using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace WinTool.Core.Updates
{
    /// <summary>
    /// A release version: 1.2.3 or a test version 1.2.3-beta.1 / 1.2.3-rc.2.
    /// A stable version is newer than its own test versions; alpha &lt; beta &lt; rc.
    /// </summary>
    public sealed class AppVersion : IComparable<AppVersion>
    {
        private static readonly Regex Pattern = new Regex(
            @"^v?(\d+)\.(\d+)\.(\d+)(?:-(alpha|beta|rc)\.?(\d+))?(?:\+.*)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private AppVersion(Version number, string stage, int stageNumber)
        {
            Number = number;
            Stage = stage;
            StageNumber = stageNumber;
        }

        public Version Number { get; }

        /// <summary>null for a stable version; otherwise alpha, beta or rc.</summary>
        public string Stage { get; }

        public int StageNumber { get; }
        public bool IsTest => Stage != null;

        /// <summary>The four-part Windows file version (D-018).</summary>
        public Version FileVersion => new Version(Number.Major, Number.Minor, Number.Build, 0);

        public static bool TryParse(string text, out AppVersion version)
        {
            version = null;
            var match = Pattern.Match((text ?? string.Empty).Trim());
            if (!match.Success) return false;
            var number = new Version(Int(match.Groups[1].Value), Int(match.Groups[2].Value), Int(match.Groups[3].Value));
            version = match.Groups[4].Success
                ? new AppVersion(number, match.Groups[4].Value.ToLowerInvariant(), Int(match.Groups[5].Value))
                : new AppVersion(number, null, 0);
            return true;
        }

        public static AppVersion Parse(string text)
        {
            if (!TryParse(text, out var version)) throw new FormatException("Not a WinTool version: " + text);
            return version;
        }

        /// <summary>This program's version, from its informational version (e.g. 1.0.0-rc.1+abc123).</summary>
        public static AppVersion Current => Parse(ProductInfo.InformationalVersion);

        public int CompareTo(AppVersion other)
        {
            if (other == null) return 1;
            var byNumber = Number.CompareTo(other.Number);
            if (byNumber != 0) return byNumber;
            if (!IsTest && !other.IsTest) return 0;
            if (!IsTest) return 1;
            if (!other.IsTest) return -1;
            var byStage = Rank(Stage).CompareTo(Rank(other.Stage));
            return byStage != 0 ? byStage : StageNumber.CompareTo(other.StageNumber);
        }

        public bool IsNewerThan(AppVersion other) => CompareTo(other) > 0;

        public override string ToString() =>
            Number.ToString(3) + (IsTest ? "-" + Stage + "." + StageNumber.ToString(CultureInfo.InvariantCulture) : string.Empty);

        private static int Rank(string stage) => stage == "alpha" ? 0 : stage == "beta" ? 1 : 2;

        private static int Int(string text) => int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
    }
}
