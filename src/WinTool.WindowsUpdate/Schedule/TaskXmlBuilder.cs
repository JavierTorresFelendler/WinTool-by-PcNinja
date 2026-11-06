using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security;
using System.Text;
using WinTool.WindowsUpdate.Storage;

namespace WinTool.WindowsUpdate.Schedule
{
    /// <summary>
    /// Builds Task Scheduler XML. Settings follow V2 (WU-50 to WU-52): SYSTEM,
    /// highest privileges, one instance, runs on battery, 6-hour limit.
    /// The task runs WinTool-by-PcNinja.exe directly (WU-54).
    /// </summary>
    public static class TaskXmlBuilder
    {
        public const string ScheduledArguments = "update run --scheduled";
        public const string RetryArguments = "update run --retry";
        public const string RunOnceArguments = "update run --as-system-task";

        public static string BuildSchedule(WindowsUpdateSettings settings, string exePath, DateTime now)
        {
            var errors = settings.Validate();
            if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));

            var triggers = new List<string>();
            if (settings.Frequency != Frequencies.Startup) triggers.Add(CalendarTrigger(settings, now));
            if (settings.Frequency == Frequencies.Startup || settings.RunAtStartup) triggers.Add(BootTrigger(settings.StartupDelayMinutes));

            return Task(
                "Runs WinTool by PcNinja to search, download and install Windows updates.",
                string.Join(Environment.NewLine, triggers),
                exePath, ScheduledArguments, settings.RunIfMissed, settings.WakeToRun, now);
        }

        public static string BuildRetry(string exePath, DateTime runAt, string reason, DateTime now)
        {
            var trigger =
                "    <TimeTrigger>" + Environment.NewLine +
                "      <StartBoundary>" + Stamp(runAt) + "</StartBoundary>" + Environment.NewLine +
                "      <Enabled>true</Enabled>" + Environment.NewLine +
                "    </TimeTrigger>";
            return Task("One-time retry for WinTool by PcNinja. Reason: " + reason, trigger, exePath, RetryArguments, true, false, now);
        }

        /// <summary>WU-56: started at once by the caller, so it has no trigger.</summary>
        public static string BuildRunOnce(string exePath, DateTime now)
        {
            return Task("One-time run of WinTool by PcNinja as SYSTEM.", string.Empty, exePath, RunOnceArguments, true, false, now);
        }

        private static string CalendarTrigger(WindowsUpdateSettings settings, DateTime now)
        {
            var start = Stamp(now.Date.Add(settings.TimeOfDay));
            var body = new StringBuilder();
            switch (settings.Frequency)
            {
                case Frequencies.Daily:
                    body.AppendLine("      <ScheduleByDay>");
                    body.AppendLine("        <DaysInterval>1</DaysInterval>");
                    body.Append("      </ScheduleByDay>");
                    break;
                case Frequencies.Weekly:
                    body.AppendLine("      <ScheduleByWeek>");
                    body.AppendLine("        <WeeksInterval>1</WeeksInterval>");
                    body.AppendLine("        <DaysOfWeek>");
                    body.AppendLine("          <" + settings.DayOfWeek + " />");
                    body.AppendLine("        </DaysOfWeek>");
                    body.Append("      </ScheduleByWeek>");
                    break;
                default:
                    body.AppendLine("      <ScheduleByMonth>");
                    body.AppendLine("        <DaysOfMonth>");
                    body.AppendLine("          <Day>" + Math.Min(28, Math.Max(1, settings.MonthlyDay)).ToString(CultureInfo.InvariantCulture) + "</Day>");
                    body.AppendLine("        </DaysOfMonth>");
                    body.AppendLine("        <Months>");
                    foreach (var month in CultureInfo.InvariantCulture.DateTimeFormat.MonthNames)
                    {
                        if (month.Length > 0) body.AppendLine("          <" + month + " />");
                    }

                    body.AppendLine("        </Months>");
                    body.Append("      </ScheduleByMonth>");
                    break;
            }

            return "    <CalendarTrigger>" + Environment.NewLine +
                   "      <StartBoundary>" + start + "</StartBoundary>" + Environment.NewLine +
                   "      <Enabled>true</Enabled>" + Environment.NewLine +
                   body + Environment.NewLine +
                   "    </CalendarTrigger>";
        }

        private static string BootTrigger(int delayMinutes)
        {
            return "    <BootTrigger>" + Environment.NewLine +
                   "      <Enabled>true</Enabled>" + Environment.NewLine +
                   "      <Delay>PT" + Math.Max(0, delayMinutes).ToString(CultureInfo.InvariantCulture) + "M</Delay>" + Environment.NewLine +
                   "    </BootTrigger>";
        }

        private static string Task(string description, string triggers, string exePath, string arguments, bool startWhenAvailable, bool wakeToRun, DateTime now)
        {
            var directory = System.IO.Path.GetDirectoryName(exePath) ?? string.Empty;
            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" + Environment.NewLine +
                   "<Task version=\"1.4\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" + Environment.NewLine +
                   "  <RegistrationInfo>" + Environment.NewLine +
                   "    <Date>" + Stamp(now) + "</Date>" + Environment.NewLine +
                   "    <Author>PcNinja</Author>" + Environment.NewLine +
                   "    <Description>" + Escape(description) + "</Description>" + Environment.NewLine +
                   "  </RegistrationInfo>" + Environment.NewLine +
                   "  <Triggers>" + (triggers.Length > 0 ? Environment.NewLine + triggers + Environment.NewLine + "  " : string.Empty) + "</Triggers>" + Environment.NewLine +
                   "  <Principals>" + Environment.NewLine +
                   "    <Principal id=\"Author\">" + Environment.NewLine +
                   "      <UserId>S-1-5-18</UserId>" + Environment.NewLine +
                   "      <RunLevel>HighestAvailable</RunLevel>" + Environment.NewLine +
                   "    </Principal>" + Environment.NewLine +
                   "  </Principals>" + Environment.NewLine +
                   "  <Settings>" + Environment.NewLine +
                   "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>" + Environment.NewLine +
                   "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" + Environment.NewLine +
                   "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>" + Environment.NewLine +
                   "    <AllowHardTerminate>true</AllowHardTerminate>" + Environment.NewLine +
                   "    <StartWhenAvailable>" + Bool(startWhenAvailable) + "</StartWhenAvailable>" + Environment.NewLine +
                   "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>" + Environment.NewLine +
                   "    <IdleSettings>" + Environment.NewLine +
                   "      <StopOnIdleEnd>false</StopOnIdleEnd>" + Environment.NewLine +
                   "      <RestartOnIdle>false</RestartOnIdle>" + Environment.NewLine +
                   "    </IdleSettings>" + Environment.NewLine +
                   "    <AllowStartOnDemand>true</AllowStartOnDemand>" + Environment.NewLine +
                   "    <Enabled>true</Enabled>" + Environment.NewLine +
                   "    <Hidden>false</Hidden>" + Environment.NewLine +
                   "    <RunOnlyIfIdle>false</RunOnlyIfIdle>" + Environment.NewLine +
                   "    <WakeToRun>" + Bool(wakeToRun) + "</WakeToRun>" + Environment.NewLine +
                   "    <ExecutionTimeLimit>PT6H</ExecutionTimeLimit>" + Environment.NewLine +
                   "    <Priority>7</Priority>" + Environment.NewLine +
                   "  </Settings>" + Environment.NewLine +
                   "  <Actions Context=\"Author\">" + Environment.NewLine +
                   "    <Exec>" + Environment.NewLine +
                   "      <Command>" + Escape(exePath) + "</Command>" + Environment.NewLine +
                   "      <Arguments>" + Escape(arguments) + "</Arguments>" + Environment.NewLine +
                   "      <WorkingDirectory>" + Escape(directory) + "</WorkingDirectory>" + Environment.NewLine +
                   "    </Exec>" + Environment.NewLine +
                   "  </Actions>" + Environment.NewLine +
                   "</Task>";
        }

        private static string Stamp(DateTime time) => time.ToString("s", CultureInfo.InvariantCulture);
        private static string Bool(bool value) => value ? "true" : "false";
        private static string Escape(string value) => SecurityElement.Escape(value ?? string.Empty);
    }
}
