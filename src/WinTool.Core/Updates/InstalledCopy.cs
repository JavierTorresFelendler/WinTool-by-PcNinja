using System;
using System.IO;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Text;

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
    /// Registered installation identity and metadata. Universal GUI choices use UniversalLaunchPolicy (D-039).
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

        /// <summary>Require consistent product/file metadata; never display a four-part file version as a product version.</summary>
        public static bool TryReadVersion(string productVersion, string fileVersion, out AppVersion version)
        {
            version = null;
            try
            {
                AppVersion product;
                Version file;
                if (!AppVersion.TryParse(productVersion, out product) || !Version.TryParse(fileVersion, out file) || product.FileVersion != file)
                    return false;
                version = product;
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is OverflowException || ex is FormatException)
            { return false; }
        }

        public static bool IsInstalled(string exePath, string installFolder)
        {
            if (string.IsNullOrWhiteSpace(exePath) || string.IsNullOrWhiteSpace(installFolder)) return false;
            try
            {
                if (!Path.IsPathRooted(exePath) || !Path.IsPathRooted(installFolder)) return false;
                return string.Equals(Normalize(exePath), Normalize(Path.Combine(Path.GetFullPath(installFolder), ReleaseFiles.Executable)), StringComparison.OrdinalIgnoreCase);
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
            try
            {
                var full = System.IO.Path.GetFullPath(path).TrimEnd('\\', '/');
                var longPath = new StringBuilder(32768);
                var length = GetLongPathNameW(full, longPath, longPath.Capacity);
                return length > 0 && length < longPath.Capacity ? longPath.ToString().TrimEnd('\\', '/') : full;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is System.IO.PathTooLongException) { return path; }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint GetLongPathNameW(string path, StringBuilder expanded, int length);
    }

    public sealed class InstalledCopyRequiredException : InvalidOperationException
    {
        public InstalledCopyRequiredException(Exception inner = null) : base("A protected local task target could not be prepared. Run as administrator and check the task-copy log. No task was registered for the portable path.", inner) { }
    }
}
