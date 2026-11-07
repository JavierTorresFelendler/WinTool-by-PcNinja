using System;

namespace WinTool.Core.Updates
{
    public enum LaunchChoice
    {
        /// <summary>Ask Install / Run portable (nothing installed, or an older version is installed).</summary>
        Ask,
        /// <summary>Start the installed copy instead: it is this version or newer (owner, 2026-11-07).</summary>
        RunInstalled,
        /// <summary>This process is the installed copy: run normally, never ask.</summary>
        RunThis
    }

    /// <summary>
    /// D-026 addition: a downloaded universal EXE first looks for an installed WinTool, so opening it
    /// again does not reinstall the same version.
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
