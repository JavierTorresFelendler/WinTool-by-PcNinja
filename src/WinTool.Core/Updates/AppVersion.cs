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

        /// <summary>
        /// The four-part Windows file version (D-018, D-024): the fourth part is the stage,
        /// alpha.N = 1000+N, beta.N = 2000+N, rc.N = 3000+N, stable = 9000. So every build
        /// has its own file version and the previous-copy names never collide.
        /// </summary>
        public Version FileVersion => new Version(Number.Major, Number.Minor, Number.Build, StageCode(1000, 9000));

        /// <summary>
        /// The MSI ProductVersion (D-024). Windows Installer compares only three parts, so the
        /// stage goes into the third: patch*1000 + (alpha.N = N, beta.N = 200+N, rc.N = 400+N, stable = 999).
        /// A final release therefore upgrades its release candidates. Patch is at most 64.
        /// </summary>
        public string MsiVersion
        {
            get
            {
                if (Number.Build > 64 || StageNumber > 199) throw new InvalidOperationException("Version " + this + " does not fit the MSI version scheme (D-024).");
                var third = Number.Build * 1000 + StageCode(200, 999);
                return Number.Major.ToString(CultureInfo.InvariantCulture) + "." + Number.Minor.ToString(CultureInfo.InvariantCulture) + "." +
                       third.ToString(CultureInfo.InvariantCulture);
            }
        }

        private int StageCode(int step, int stable) =>
            !IsTest ? stable : (Stage == "alpha" ? 0 : Stage == "beta" ? step : 2 * step) + StageNumber + (step == 1000 ? 1000 : 0);

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
