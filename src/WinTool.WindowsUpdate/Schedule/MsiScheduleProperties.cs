using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WinTool.WindowsUpdate.Storage;

namespace WinTool.WindowsUpdate.Schedule
{
    /// <summary>
    /// The MSI properties that make the installer create this schedule (D-014): a portable
    /// copy hands its settings to the MSI, whose ApplySchedule action runs the installed
    /// EXE's "schedule set". Booleans are "1" or empty, as the MSI's checkboxes write them.
    /// </summary>
    public static class MsiScheduleProperties
    {
        public static IReadOnlyList<KeyValuePair<string, string>> From(WindowsUpdateSettings s) => new[]
        {
            Pair("WINTOOL_ENABLE_SCHEDULE", true),
            Pair("WINTOOL_FREQUENCY", s.Frequency),
            Pair("WINTOOL_TIME", s.Time),
            Pair("WINTOOL_DAY_OF_WEEK", s.DayOfWeek),
            Pair("WINTOOL_MONTHLY_DAY", s.MonthlyDay),
            Pair("WINTOOL_RUN_AT_STARTUP", s.RunAtStartup),
            Pair("WINTOOL_STARTUP_DELAY", s.StartupDelayMinutes),
            Pair("WINTOOL_RUN_IF_MISSED", s.RunIfMissed),
            Pair("WINTOOL_WAKE_TO_RUN", s.WakeToRun),
            Pair("WINTOOL_ALLOW_FIRMWARE", s.InstallFirmwareUpdates),
            Pair("WINTOOL_REBOOT_PROMPT", s.ShowRebootPrompt),
            Pair("WINTOOL_ENABLE_AUTORETRY", s.AutoRetryEnabled),
            Pair("WINTOOL_RETRY_INITIAL_DELAY", s.RetryInitialDelayMinutes),
            Pair("WINTOOL_RETRY_MAX_ATTEMPTS", s.RetryMaxAttempts),
            Pair("WINTOOL_RETRY_BACKOFF", s.RetryBackoffMultiplier),
            Pair("WINTOOL_MINIMUM_COOLDOWN", s.MinimumCooldownMinutes),
        };

        /// <summary>msiexec arguments: NAME="value" for each property.</summary>
        public static string CommandLine(WindowsUpdateSettings settings) =>
            string.Join(" ", From(settings).Select(p => p.Key + "=\"" + p.Value.Replace("\"", string.Empty) + "\""));

        private static KeyValuePair<string, string> Pair(string name, bool value) => new KeyValuePair<string, string>(name, value ? "1" : string.Empty);
        private static KeyValuePair<string, string> Pair(string name, int value) => new KeyValuePair<string, string>(name, value.ToString(CultureInfo.InvariantCulture));
        private static KeyValuePair<string, string> Pair(string name, string value) => new KeyValuePair<string, string>(name, value ?? string.Empty);
    }
}
