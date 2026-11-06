using System;
using System.Diagnostics;
using WinTool.Core.Diagnostics;
using WinTool.WindowsUpdate.Engine;

namespace WinTool.WindowsUpdate.Platform
{
    /// <summary>
    /// Every message goes to the WinTool log. Warnings, errors and the main
    /// milestones also go to the Windows Application event log, where
    /// monitoring tools read them (WU-83). Event log failures never stop a run.
    /// </summary>
    public sealed class RunLog : IRunLog
    {
        public const string EventSource = "WinTool by PcNinja";

        private static readonly int[] InformationEventsForEventLog =
        {
            EventIds.RunStarted, EventIds.RunFinished, EventIds.InstallResult, EventIds.ResetFinished,
            EventIds.ScheduleSaved, EventIds.ScheduleRemoved, EventIds.RetryScheduled
        };

        private readonly AppLog appLog;
        private readonly bool useEventLog;
        private bool eventLogFailed;

        public RunLog(AppLog appLog, bool useEventLog)
        {
            this.appLog = appLog ?? AppLog.Current;
            this.useEventLog = useEventLog;
        }

        public event Action<int, EventLevel, string> Written;

        public void Write(int eventId, EventLevel level, string message)
        {
            switch (level)
            {
                case EventLevel.Error: appLog.Error("WindowsUpdate", message); break;
                case EventLevel.Warning: appLog.Warn("WindowsUpdate", message); break;
                default: appLog.Info("WindowsUpdate", message); break;
            }

            Written?.Invoke(eventId, level, message);
            if (useEventLog && (level != EventLevel.Information || Array.IndexOf(InformationEventsForEventLog, eventId) >= 0))
            {
                WriteEvent(eventId, level, message);
            }
        }

        private void WriteEvent(int eventId, EventLevel level, string message)
        {
            if (eventLogFailed) return;
            try
            {
                if (!EventLog.SourceExists(EventSource)) EventLog.CreateEventSource(EventSource, "Application");
                var type = level == EventLevel.Error ? EventLogEntryType.Error : level == EventLevel.Warning ? EventLogEntryType.Warning : EventLogEntryType.Information;
                EventLog.WriteEntry(EventSource, message, type, eventId);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is InvalidOperationException || ex is ArgumentException || ex is System.ComponentModel.Win32Exception)
            {
                eventLogFailed = true;
                appLog.Warn("WindowsUpdate", "Windows event log is not available: " + ex.Message);
            }
        }
    }
}
