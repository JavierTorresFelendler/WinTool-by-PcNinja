using System;
using WinTool.WindowsUpdate.Storage;

namespace WinTool.App.Localization
{
    /// <summary>A Windows Update schedule in the window's language ("every Wednesday at 05:30").</summary>
    internal static class ScheduleWords
    {
        public static string Describe(WindowsUpdateSettings settings, Loc t)
        {
            if (settings == null || !settings.ScheduleEnabled) return t["Dash_ScheduleOff"];
            switch (settings.Frequency)
            {
                case Frequencies.Daily: return t.Format("WU_When_Daily", settings.Time);
                case Frequencies.Weekly: return t.Format("WU_When_Weekly", DayName(settings.DayOfWeek, t), settings.Time);
                case Frequencies.Monthly: return t.Format("WU_When_Monthly", settings.MonthlyDay, settings.Time);
                default: return t["WU_When_Startup"];
            }
        }

        private static string DayName(string day, Loc t)
        {
            DayOfWeek parsed;
            return Enum.TryParse(day, true, out parsed) ? t.Culture.DateTimeFormat.GetDayName(parsed) : day;
        }
    }
}
