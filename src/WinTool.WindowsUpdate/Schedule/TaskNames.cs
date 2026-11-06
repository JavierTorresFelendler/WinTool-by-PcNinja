using System;

namespace WinTool.WindowsUpdate.Schedule
{
    /// <summary>Scheduled task names (WU-50, WU-53, D-015).</summary>
    public static class TaskNames
    {
        public const string Folder = @"\PcNinja";
        public const string Main = "WinTool by PcNinja";
        public const string Retry = "WinTool by PcNinja Retry";
        public const string RunOnce = "WinTool by PcNinja Run Once";

        /// <summary>WinUpdate Tool V1/V2 tasks, which WinTool takes over (D-015).</summary>
        public const string LegacyPrefix = "PcNinja WinUpdate Tool";

        /// <summary>Tasks this product owns, in any folder.</summary>
        public static bool IsOwn(string taskName) =>
            !string.IsNullOrEmpty(taskName) && taskName.StartsWith(Main, StringComparison.OrdinalIgnoreCase);

        public static bool IsLegacyWinUpdate(string taskName) =>
            !string.IsNullOrEmpty(taskName) && taskName.StartsWith(LegacyPrefix, StringComparison.OrdinalIgnoreCase);
    }
}
