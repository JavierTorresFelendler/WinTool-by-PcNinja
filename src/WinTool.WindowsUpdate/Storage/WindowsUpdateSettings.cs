using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.Serialization;

namespace WinTool.WindowsUpdate.Storage
{
    public static class Frequencies
    {
        public const string Daily = "Daily";
        public const string Weekly = "Weekly";
        public const string Monthly = "Monthly";
        public const string Startup = "Startup";
        public static readonly string[] All = { Daily, Weekly, Monthly, Startup };
    }

    /// <summary>
    /// Machine-wide Windows Update settings (WU-80). Defaults match the
    /// WinUpdate V2 MSI defaults; one startup delay default everywhere (WU-51).
    /// </summary>
    [DataContract]
    public sealed class WindowsUpdateSettings
    {
        public const int CurrentSchemaVersion = 1;

        public WindowsUpdateSettings()
        {
            SchemaVersion = CurrentSchemaVersion;
            ScheduleEnabled = false;
            Frequency = Frequencies.Monthly;
            Time = "03:00";
            DayOfWeek = "Sunday";
            MonthlyDay = 15;
            RunAtStartup = false;
            StartupDelayMinutes = 5;
            RunIfMissed = true;
            WakeToRun = false;
            ShowRebootPrompt = true;
            InstallFirmwareUpdates = false;
            AutoRetryEnabled = true;
            RetryInitialDelayMinutes = 5;
            RetryMaxAttempts = 3;
            RetryBackoffMultiplier = 2;
            MinimumCooldownMinutes = 5;
        }

        [DataMember(Name = "schemaVersion", Order = 0)] public int SchemaVersion { get; set; }
        [DataMember(Name = "scheduleEnabled", Order = 1)] public bool ScheduleEnabled { get; set; }
        [DataMember(Name = "frequency", Order = 2)] public string Frequency { get; set; }
        [DataMember(Name = "time", Order = 3)] public string Time { get; set; }
        [DataMember(Name = "dayOfWeek", Order = 4)] public string DayOfWeek { get; set; }
        [DataMember(Name = "monthlyDay", Order = 5)] public int MonthlyDay { get; set; }
        [DataMember(Name = "runAtStartup", Order = 6)] public bool RunAtStartup { get; set; }
        [DataMember(Name = "startupDelayMinutes", Order = 7)] public int StartupDelayMinutes { get; set; }
        [DataMember(Name = "runIfMissed", Order = 8)] public bool RunIfMissed { get; set; }
        [DataMember(Name = "wakeToRun", Order = 9)] public bool WakeToRun { get; set; }
        [DataMember(Name = "showRebootPrompt", Order = 10)] public bool ShowRebootPrompt { get; set; }
        [DataMember(Name = "installFirmwareUpdates", Order = 11)] public bool InstallFirmwareUpdates { get; set; }
        [DataMember(Name = "autoRetryEnabled", Order = 12)] public bool AutoRetryEnabled { get; set; }
        [DataMember(Name = "retryInitialDelayMinutes", Order = 13)] public int RetryInitialDelayMinutes { get; set; }
        [DataMember(Name = "retryMaxAttempts", Order = 14)] public int RetryMaxAttempts { get; set; }
        [DataMember(Name = "retryBackoffMultiplier", Order = 15)] public int RetryBackoffMultiplier { get; set; }
        [DataMember(Name = "minimumCooldownMinutes", Order = 16)] public int MinimumCooldownMinutes { get; set; }

        public TimeSpan TimeOfDay
        {
            get
            {
                TimeSpan value;
                return TryParseTime(Time, out value) ? value : new TimeSpan(3, 0, 0);
            }
        }

        public static bool TryParseTime(string text, out TimeSpan value)
        {
            DateTime parsed;
            if (!string.IsNullOrWhiteSpace(text)
                && DateTime.TryParseExact(text.Trim(), new[] { "HH:mm", "H:mm" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                value = parsed.TimeOfDay;
                return true;
            }

            value = TimeSpan.Zero;
            return false;
        }

        /// <summary>Problems in English, for logs, CLI and MSI. Empty = valid. Never fixed silently (MSI spec).</summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            if (Array.IndexOf(Frequencies.All, Frequency) < 0) errors.Add("Frequency must be Daily, Weekly, Monthly or Startup.");
            TimeSpan ignored;
            if (!TryParseTime(Time, out ignored)) errors.Add("Time must be HH:mm, for example 03:00.");
            if (Frequency == Frequencies.Weekly && !IsDayName(DayOfWeek)) errors.Add("DayOfWeek must be a day name, for example Sunday.");
            if (Frequency == Frequencies.Monthly && (MonthlyDay < 1 || MonthlyDay > 28)) errors.Add("MonthlyDay must be between 1 and 28.");
            if (StartupDelayMinutes < 0 || StartupDelayMinutes > 1440) errors.Add("StartupDelayMinutes must be between 0 and 1440.");
            if (RetryInitialDelayMinutes < 1 || RetryInitialDelayMinutes > 360) errors.Add("RetryInitialDelayMinutes must be between 1 and 360.");
            if (RetryMaxAttempts < 0 || RetryMaxAttempts > 10) errors.Add("RetryMaxAttempts must be between 0 and 10.");
            if (RetryBackoffMultiplier < 1 || RetryBackoffMultiplier > 10) errors.Add("RetryBackoffMultiplier must be between 1 and 10.");
            if (MinimumCooldownMinutes < 0 || MinimumCooldownMinutes > 1440) errors.Add("MinimumCooldownMinutes must be between 0 and 1440.");
            return errors;
        }

        public static bool IsDayName(string value)
        {
            System.DayOfWeek day;
            return !string.IsNullOrWhiteSpace(value) && Enum.TryParse(value, false, out day) && !char.IsDigit(value[0]);
        }

        public WindowsUpdateSettings Clone() => (WindowsUpdateSettings)MemberwiseClone();
    }
}
