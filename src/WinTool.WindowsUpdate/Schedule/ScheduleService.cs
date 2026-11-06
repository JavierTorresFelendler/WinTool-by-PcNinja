using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using WinTool.WindowsUpdate.Engine;
using WinTool.WindowsUpdate.Storage;

namespace WinTool.WindowsUpdate.Schedule
{
    public sealed class ScheduleStatus
    {
        public bool Enabled { get; set; }
        public TaskEntry Main { get; set; }
        public TaskEntry Retry { get; set; }
        public bool? MatchesSettings { get; set; }

        /// <summary>When the task does not match: which parts differ, expected vs registered.</summary>
        public IReadOnlyList<string> Differences { get; set; }
    }

    /// <summary>Saves, removes and checks the Windows Update schedule (WU-50 to WU-56, WU-62).</summary>
    public sealed class ScheduleService : IRetryScheduler
    {
        private readonly ITaskSchedulerService tasks;
        private readonly IRunLog log;
        private readonly string exePath;
        private readonly Func<DateTime> clock;

        public ScheduleService(ITaskSchedulerService tasks, IRunLog log, string exePath, Func<DateTime> clock)
        {
            this.tasks = tasks;
            this.log = log;
            this.exePath = exePath;
            this.clock = clock ?? (() => DateTime.Now);
        }

        /// <summary>
        /// WU-53: removes every WinTool task in any folder first, so no
        /// duplicate can survive, then registers the task when enabled.
        /// Fails, without registering, when an old task cannot be removed.
        /// </summary>
        public void Apply(WindowsUpdateSettings settings)
        {
            var errors = settings.Validate();
            if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));

            var xml = settings.ScheduleEnabled ? TaskXmlBuilder.BuildSchedule(settings, exePath, clock()) : null;
            RemoveOwnTasks(keepRetry: false);
            if (xml == null)
            {
                log.Write(EventIds.ScheduleRemoved, EventLevel.Information, "Windows Update schedule turned off.");
                return;
            }

            tasks.Register(TaskNames.Main, xml);
            log.Write(EventIds.ScheduleSaved, EventLevel.Information, "Windows Update schedule saved: " + Describe(settings) + ".");
        }

        public void RemoveAll() => RemoveOwnTasks(keepRetry: false);

        public ScheduleStatus GetStatus(WindowsUpdateSettings settings)
        {
            var main = tasks.Get(TaskNames.Main);
            var status = new ScheduleStatus { Enabled = settings.ScheduleEnabled, Main = main, Retry = tasks.Get(TaskNames.Retry) };
            if (settings.ScheduleEnabled)
            {
                var differences = main == null ? new List<string> { "The task is missing." } : Compare(main.Xml, settings);
                status.MatchesSettings = differences.Count == 0;
                status.Differences = differences;
            }

            return status;
        }

        public void ScheduleRetry(DateTime runAt, string reason, int retryNumber)
        {
            tasks.Register(TaskNames.Retry, TaskXmlBuilder.BuildRetry(exePath, runAt, reason, clock()));
        }

        public void CancelRetry()
        {
            tasks.Delete(TaskNames.Folder, TaskNames.Retry);
        }

        /// <summary>WU-56: start a one-time run as SYSTEM now.</summary>
        public void StartRunOnce()
        {
            tasks.Register(TaskNames.RunOnce, TaskXmlBuilder.BuildRunOnce(exePath, clock()));
            tasks.Run(TaskNames.RunOnce);
        }

        public static string Describe(WindowsUpdateSettings settings)
        {
            string when;
            switch (settings.Frequency)
            {
                case Frequencies.Daily: when = "daily at " + settings.Time; break;
                case Frequencies.Weekly: when = "weekly on " + settings.DayOfWeek + " at " + settings.Time; break;
                case Frequencies.Monthly: when = "monthly on day " + settings.MonthlyDay + " at " + settings.Time; break;
                default: when = "at startup"; break;
            }

            if (settings.Frequency != Frequencies.Startup && settings.RunAtStartup) when += ", and at startup";
            if (settings.Frequency == Frequencies.Startup || settings.RunAtStartup) when += " after " + settings.StartupDelayMinutes + " minute(s)";
            return when;
        }

        /// <summary>
        /// Compares what matters in a registered task with the settings:
        /// program, arguments, triggers (kind, time of day, days), run-if-missed
        /// and wake. Task Scheduler may reformat the XML, so dates and layout are ignored.
        /// </summary>
        public bool Matches(string registeredXml, WindowsUpdateSettings settings) => Compare(registeredXml, settings).Count == 0;

        /// <summary>Empty when they match; otherwise "expected | registered" for each differing part.</summary>
        public List<string> Compare(string registeredXml, WindowsUpdateSettings settings)
        {
            if (string.IsNullOrWhiteSpace(registeredXml)) return new List<string> { "The task has no definition." };
            try
            {
                var expected = Signature(TaskXmlBuilder.BuildSchedule(settings, exePath, clock()));
                var actual = Signature(registeredXml);
                var differences = new List<string>();
                for (var i = 0; i < Math.Max(expected.Count, actual.Count); i++)
                {
                    var e = i < expected.Count ? expected[i] : "(none)";
                    var r = i < actual.Count ? actual[i] : "(none)";
                    if (!string.Equals(e, r, StringComparison.Ordinal)) differences.Add("expected " + e + " | registered " + r);
                }

                return differences;
            }
            catch (Exception ex) when (ex is System.Xml.XmlException || ex is ArgumentException)
            {
                return new List<string> { "The task definition could not be read: " + ex.Message };
            }
        }

        internal static List<string> Signature(string xml)
        {
            var doc = XDocument.Parse(xml);
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            var parts = new List<string>
            {
                ((string)doc.Descendants(ns + "Command").FirstOrDefault() ?? string.Empty).Trim().ToLowerInvariant(),
                ((string)doc.Descendants(ns + "Arguments").FirstOrDefault() ?? string.Empty).Trim(),
                "missed=" + ((string)doc.Descendants(ns + "StartWhenAvailable").FirstOrDefault() ?? "false"),
                "wake=" + ((string)doc.Descendants(ns + "WakeToRun").FirstOrDefault() ?? "false")
            };

            var triggers = doc.Descendants(ns + "Triggers").Elements().Select(trigger =>
            {
                var text = trigger.Name.LocalName;
                var start = (string)trigger.Element(ns + "StartBoundary");
                DateTime parsed;
                if (start != null && start.Length >= 19 && DateTime.TryParse(start.Substring(0, 19), CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                {
                    text += "@" + parsed.ToString("HH:mm", CultureInfo.InvariantCulture);
                }

                var delay = (string)trigger.Element(ns + "Delay");
                if (delay != null) text += "+" + delay;
                foreach (var schedule in trigger.Elements().Where(e => e.Name.LocalName.StartsWith("ScheduleBy", StringComparison.Ordinal)))
                {
                    text += "|" + schedule.Name.LocalName + ":" + string.Join(",",
                        schedule.Descendants().Where(d => !d.HasElements).Select(d => d.Name.LocalName + "=" + d.Value.Trim()));
                }

                return text;
            }).OrderBy(t => t, StringComparer.Ordinal);

            return parts.Concat(triggers).ToList();
        }

        private void RemoveOwnTasks(bool keepRetry)
        {
            var failures = new List<string>();
            foreach (var task in tasks.Find(TaskNames.IsOwn))
            {
                if (keepRetry && task.Name == TaskNames.Retry) continue;
                try
                {
                    tasks.Delete(task.Folder, task.Name);
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException))
                {
                    failures.Add(task.FullPath + ": " + ex.Message);
                }
            }

            if (failures.Count > 0)
            {
                var message = "Existing WinTool scheduled tasks could not be replaced: " + string.Join(" ", failures);
                log.Write(EventIds.ScheduleFailed, EventLevel.Error, message);
                throw new InvalidOperationException(message);
            }
        }
    }
}
