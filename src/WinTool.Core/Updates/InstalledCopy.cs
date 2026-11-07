using System;

namespace WinTool.Core.Updates
{
    public enum LaunchChoice
    {
        /// <summary>Ask Install / Run portable; Install runs the installer (nothing installed, or an older version).</summary>
        Ask,
        /// <summary>When Install is chosen, start the installed copy: it is this version or newer (owner, 2026-11-07).</summary>
        RunInstalled,
        /// <summary>This process is the installed copy: run normally, never ask.</summary>
        RunThis
    }

    /// <summary>
    /// D-026 addition: the downloaded universal EXE always asks; when Install is chosen and this version
    /// or a newer one is already installed, it starts that copy instead of reinstalling it.
    /// </summary>
    public static class InstalledCopy
    {
        public static LaunchChoice Decide(string thisExe, string installedExe, Version installedFileVersion, Version thisFileVersion)
        {
            if (string.IsNullOrEmpty(installedExe) || installedFileVersion == null) return LaunchChoice.Ask;
            if (string.Equals(Normalize(thisExe), Normalize(installedExe), StringComparison.OrdinalIgnoreCase)) return LaunchChoice.RunThis;
            return installedFileVersion >= thisFileVersion ? LaunchChoice.RunInstalled : LaunchChoice.Ask;
        }

        private static string Normalize(string path)
        {
            try { return System.IO.Path.GetFullPath(path).TrimEnd('\\', '/'); }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is System.IO.PathTooLongException) { return path; }
        }
    }
}
