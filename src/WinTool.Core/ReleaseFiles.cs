using System;
using System.Globalization;

namespace WinTool.Core
{
    /// <summary>Official release file names (decision D-018). Never put a version in them.</summary>
    public static class ReleaseFiles
    {
        public const string BaseName = "WinTool-by-PcNinja";
        public const string Executable = BaseName + ".exe";
        public const string Installer = BaseName + ".msi";

        /// <summary>
        /// Name the portable updater gives the replaced copy, so the previous
        /// version stays next to the new one, e.g. WinTool-by-PcNinja-v1.2.0.0.exe.
        /// </summary>
        public static string PreviousCopyName(Version version)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            var full = new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
            return string.Format(CultureInfo.InvariantCulture, "{0}-v{1}.exe", BaseName, full);
        }
    }
}
