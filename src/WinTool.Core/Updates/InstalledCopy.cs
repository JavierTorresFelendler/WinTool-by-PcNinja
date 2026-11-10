using System;
using System.IO;
using Microsoft.Win32;

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
        public static string ReadInstallFolder()
        {
            try
            {
                using (var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var key = machine.OpenSubKey(@"SOFTWARE\PcNinja\WinTool"))
                    return key?.GetValue("InstallFolder") as string;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException)
            {
                return null;
            }
        }

        public static bool IsInstalled(string exePath) => IsInstalled(exePath, ReadInstallFolder());

        public static bool IsInstalled(string exePath, string installFolder)
        {
            if (string.IsNullOrWhiteSpace(exePath) || string.IsNullOrWhiteSpace(installFolder)) return false;
            try
            {
                if (!Path.IsPathRooted(exePath) || !Path.IsPathRooted(installFolder)) return false;
                return string.Equals(Path.GetFullPath(exePath), Path.Combine(Path.GetFullPath(installFolder), ReleaseFiles.Executable), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return false;
            }
        }

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

    public sealed class InstalledCopyRequiredException : InvalidOperationException
    {
        public InstalledCopyRequiredException() : base("Task registration requires the installed copy of WinTool. Install WinTool using the MSI or the universal EXE's Install choice, then run the command from the installed copy.") { }
    }
}
