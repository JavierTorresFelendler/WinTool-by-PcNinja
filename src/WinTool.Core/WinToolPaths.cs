using System;
using System.IO;

namespace WinTool.Core
{
    /// <summary>
    /// Where WinTool keeps its own data. Normally %ProgramData%\PcNinja\WinTool (machine data:
    /// logs, Windows Update state, upgrade state, downloaded updates) and
    /// %LocalAppData%\PcNinja\WinTool (per-user settings).
    /// The environment variable WINTOOL_DATA_ROOT, when it holds an absolute path, moves both
    /// under that folder (&lt;root&gt;\ProgramData and &lt;root&gt;\User) so a test run never touches
    /// the real data. It does not move what belongs to other programs, such as the old
    /// WinUpdate Tool's configuration.
    /// </summary>
    public static class WinToolPaths
    {
        public const string DataRootVariable = "WINTOOL_DATA_ROOT";

        /// <summary>Machine-wide data folder.</summary>
        public static string MachineData => Resolve(Environment.GetEnvironmentVariable(DataRootVariable), Environment.GetFolderPath).MachineData;

        /// <summary>Per-user data folder.</summary>
        public static string UserData => Resolve(Environment.GetEnvironmentVariable(DataRootVariable), Environment.GetFolderPath).UserData;

        /// <summary>The test root in use, or null when WinTool uses its normal folders.</summary>
        public static string DataRootOverride => NormalizeRoot(Environment.GetEnvironmentVariable(DataRootVariable));

        /// <summary>Prepares a data directory, enforcing the machine policy only inside WinTool's real machine-data tree.</summary>
        public static string EnsureMachineDataDirectory(string directory)
        {
            var fullPath = Path.GetFullPath(directory);
            var machineRoot = Path.GetFullPath(Resolve(null, Environment.GetFolderPath).MachineData).TrimEnd('\\', '/');
            if (DataRootOverride == null && (string.Equals(fullPath, machineRoot, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(machineRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                Security.MachineDataDirectory.Ensure(machineRoot, fullPath);
            else
                Directory.CreateDirectory(fullPath);
            return fullPath;
        }

        public static DataFolders Resolve(string dataRoot, Func<Environment.SpecialFolder, string> specialFolder)
        {
            var root = NormalizeRoot(dataRoot);
            if (root != null)
                return new DataFolders(Path.Combine(root, "ProgramData"), Path.Combine(root, "User"));
            return new DataFolders(
                Path.Combine(specialFolder(Environment.SpecialFolder.CommonApplicationData), "PcNinja", "WinTool"),
                Path.Combine(specialFolder(Environment.SpecialFolder.LocalApplicationData), "PcNinja", "WinTool"));
        }

        /// <summary>Only an absolute path counts; anything else is ignored so a typo cannot scatter data into the current folder.</summary>
        private static string NormalizeRoot(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var trimmed = value.Trim().Trim('"');
            try
            {
                return Path.IsPathRooted(trimmed) && !IsDriveRelative(trimmed) ? Path.GetFullPath(trimmed) : null;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null;
            }
        }

        // "C:folder" is rooted on Windows but relative to that drive's current folder.
        private static bool IsDriveRelative(string path) =>
            path.Length >= 2 && path[1] == ':' && (path.Length == 2 || (path[2] != '\\' && path[2] != '/'));
    }

    public sealed class DataFolders
    {
        public DataFolders(string machineData, string userData)
        {
            MachineData = machineData;
            UserData = userData;
        }

        public string MachineData { get; }
        public string UserData { get; }
    }
}
