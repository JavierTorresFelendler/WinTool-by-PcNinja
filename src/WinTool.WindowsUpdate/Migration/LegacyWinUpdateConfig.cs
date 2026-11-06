using System;
using System.IO;
using System.Text.RegularExpressions;
using WinTool.WindowsUpdate.Storage;

namespace WinTool.WindowsUpdate.Migration
{
    /// <summary>
    /// Reads the WinUpdate Tool V1/V2 config.json for the takeover (D-015).
    /// The V2 file is PowerShell ConvertTo-Json output (UTF-8 with BOM,
    /// PascalCase names), parsed by name so unknown fields are ignored.
    /// </summary>
    public static class LegacyWinUpdateConfig
    {
        public static string DefaultPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PcNinja", "WinUpdateTool", "config.json");

        public static WindowsUpdateSettings Import(string json)
        {
            var result = new WindowsUpdateSettings();
            if (string.IsNullOrWhiteSpace(json)) return result;

            result.ScheduleEnabled = Bool(json, "Enabled", result.ScheduleEnabled);
            result.Frequency = String(json, "Frequency", result.Frequency);
            result.Time = String(json, "Time", result.Time);
            result.DayOfWeek = String(json, "DayOfWeek", result.DayOfWeek);
            result.MonthlyDay = Int(json, "MonthlyDay", result.MonthlyDay);
            result.RunAtStartup = Bool(json, "RunAtStartup", result.RunAtStartup);
            result.StartupDelayMinutes = Int(json, "StartupDelayMinutes", result.StartupDelayMinutes);
            result.RunIfMissed = Bool(json, "RunIfMissed", result.RunIfMissed);
            result.WakeToRun = Bool(json, "WakeToRun", result.WakeToRun);
            result.ShowRebootPrompt = Bool(json, "ShowRebootPrompt", result.ShowRebootPrompt);
            result.InstallFirmwareUpdates = Bool(json, "InstallFirmwareUpdates", result.InstallFirmwareUpdates);
            result.AutoRetryEnabled = Bool(json, "AutoRetryEnabled", result.AutoRetryEnabled);
            result.RetryInitialDelayMinutes = Int(json, "RetryInitialDelayMinutes", result.RetryInitialDelayMinutes);
            result.RetryMaxAttempts = Int(json, "RetryMaxAttempts", result.RetryMaxAttempts);
            result.RetryBackoffMultiplier = Int(json, "RetryBackoffMultiplier", result.RetryBackoffMultiplier);
            result.MinimumCooldownMinutes = Int(json, "MinimumCooldownMinutes", result.MinimumCooldownMinutes);

            // A value WinTool would reject falls back to the default rather than blocking the takeover.
            var defaults = new WindowsUpdateSettings();
            if (Array.IndexOf(Frequencies.All, result.Frequency) < 0) result.Frequency = defaults.Frequency;
            TimeSpan ignored;
            if (!WindowsUpdateSettings.TryParseTime(result.Time, out ignored)) result.Time = defaults.Time;
            if (!WindowsUpdateSettings.IsDayName(result.DayOfWeek)) result.DayOfWeek = defaults.DayOfWeek;
            result.MonthlyDay = Math.Min(28, Math.Max(1, result.MonthlyDay));
            return result;
        }

        private static Match Find(string json, string name, string valuePattern)
        {
            return Regex.Match(json, "\"" + Regex.Escape(name) + "\"\\s*:\\s*" + valuePattern, RegexOptions.IgnoreCase);
        }

        private static bool Bool(string json, string name, bool fallback)
        {
            var match = Find(json, name, "(true|false)");
            return match.Success ? string.Equals(match.Groups[1].Value, "true", StringComparison.OrdinalIgnoreCase) : fallback;
        }

        private static int Int(string json, string name, int fallback)
        {
            var match = Find(json, name, "(-?\\d+)");
            int value;
            return match.Success && int.TryParse(match.Groups[1].Value, out value) ? value : fallback;
        }

        private static string String(string json, string name, string fallback)
        {
            var match = Find(json, name, "\"([^\"]*)\"");
            return match.Success ? match.Groups[1].Value : fallback;
        }
    }
}
