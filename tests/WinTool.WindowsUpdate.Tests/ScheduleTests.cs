using System;
using System.Linq;
using System.Xml.Linq;
using WinTool.WindowsUpdate.Schedule;
using WinTool.WindowsUpdate.Storage;
using Xunit;

namespace WinTool.WindowsUpdate.Tests
{
    public class TaskXmlTests
    {
        private const string Exe = @"C:\Program Files\PcNinja\WinTool\WinTool-by-PcNinja.exe";
        private static readonly DateTime Now = new DateTime(2026, 11, 6, 10, 0, 0);
        private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

        private static XDocument Build(Action<WindowsUpdateSettings> change)
        {
            var settings = new WindowsUpdateSettings { ScheduleEnabled = true };
            change(settings);
            return XDocument.Parse(TaskXmlBuilder.BuildSchedule(settings, Exe, Now));
        }

        [Fact]
        public void Runs_the_exe_directly_as_system()
        {
            var doc = Build(s => { });
            Assert.Equal(Exe, (string)doc.Descendants(Ns + "Command").Single());
            Assert.Equal("update run --scheduled", (string)doc.Descendants(Ns + "Arguments").Single());
            Assert.Equal("S-1-5-18", (string)doc.Descendants(Ns + "UserId").Single());
            Assert.Equal("IgnoreNew", (string)doc.Descendants(Ns + "MultipleInstancesPolicy").Single());
            Assert.Equal("PT6H", (string)doc.Descendants(Ns + "ExecutionTimeLimit").Single());
        }

        [Fact]
        public void Daily_trigger_at_the_chosen_time()
        {
            var trigger = Build(s => { s.Frequency = "Daily"; s.Time = "04:15"; }).Descendants(Ns + "CalendarTrigger").Single();
            Assert.Equal("2026-11-06T04:15:00", (string)trigger.Element(Ns + "StartBoundary"));
            Assert.NotNull(trigger.Element(Ns + "ScheduleByDay"));
        }

        [Fact]
        public void Weekly_trigger_on_the_chosen_day()
        {
            var doc = Build(s => { s.Frequency = "Weekly"; s.DayOfWeek = "Friday"; });
            Assert.NotNull(doc.Descendants(Ns + "Friday").SingleOrDefault());
        }

        [Fact]
        public void Monthly_trigger_on_the_chosen_day_every_month()
        {
            var doc = Build(s => { s.Frequency = "Monthly"; s.MonthlyDay = 20; });
            Assert.Equal("20", (string)doc.Descendants(Ns + "Day").Single());
            Assert.Equal(12, doc.Descendants(Ns + "Months").Single().Elements().Count());
        }

        [Fact]
        public void Startup_frequency_has_only_a_boot_trigger_with_delay()
        {
            var triggers = Build(s => { s.Frequency = "Startup"; s.StartupDelayMinutes = 7; }).Descendants(Ns + "Triggers").Single().Elements().ToList();
            var boot = Assert.Single(triggers);
            Assert.Equal("BootTrigger", boot.Name.LocalName);
            Assert.Equal("PT7M", (string)boot.Element(Ns + "Delay"));
        }

        [Fact]
        public void Run_at_startup_adds_a_boot_trigger()
        {
            var triggers = Build(s => { s.Frequency = "Daily"; s.RunAtStartup = true; }).Descendants(Ns + "Triggers").Single().Elements().Select(e => e.Name.LocalName).ToList();
            Assert.Equal(new[] { "CalendarTrigger", "BootTrigger" }, triggers);
        }

        [Fact]
        public void Run_if_missed_and_wake_map_to_task_settings()
        {
            var doc = Build(s => { s.RunIfMissed = false; s.WakeToRun = true; });
            Assert.Equal("false", (string)doc.Descendants(Ns + "StartWhenAvailable").Single());
            Assert.Equal("true", (string)doc.Descendants(Ns + "WakeToRun").Single());
        }

        [Fact]
        public void Invalid_settings_are_refused()
        {
            Assert.Throws<ArgumentException>(() => TaskXmlBuilder.BuildSchedule(new WindowsUpdateSettings { Time = "nope" }, Exe, Now));
        }

        [Fact]
        public void Paths_and_reasons_are_xml_escaped()
        {
            var xml = TaskXmlBuilder.BuildRetry(@"C:\Tools & More\WinTool-by-PcNinja.exe", Now.AddMinutes(5), "network <down>", Now);
            var doc = XDocument.Parse(xml);
            Assert.Equal(@"C:\Tools & More\WinTool-by-PcNinja.exe", (string)doc.Descendants(Ns + "Command").Single());
            Assert.Equal("2026-11-06T10:05:00", (string)doc.Descendants(Ns + "TimeTrigger").Single().Element(Ns + "StartBoundary"));
            Assert.Equal("update run --retry", (string)doc.Descendants(Ns + "Arguments").Single());
        }
    }

    public class ScheduleServiceTests
    {
        private const string Exe = @"C:\Program Files\PcNinja\WinTool\WinTool-by-PcNinja.exe";

        private static ScheduleService Service(FakeTasks tasks) => new ScheduleService(tasks, new ListLog(), Exe, () => new DateTime(2026, 11, 6, 10, 0, 0), path => path);

        [Fact]
        public void Refresh_repoints_retry_only_tasks_without_creating_a_main_schedule()
        {
            var tasks = new FakeTasks();
            var oldXml = TaskXmlBuilder.BuildRetry(@"C:\old-worker.exe", DateTime.Now.AddHours(1), "fixture", DateTime.Now);
            tasks.Register(TaskNames.Retry, oldXml);
            Service(tasks).RefreshExisting();
            var updated = Assert.Single(tasks.All);
            Assert.Equal(TaskNames.Retry, updated.Name);
            Assert.Equal(Exe, (string)XDocument.Parse(updated.Xml).Descendants().Single(element => element.Name.LocalName == "Command"));
            Assert.Equal(ScheduleService.Signature(oldXml).Skip(1), ScheduleService.Signature(updated.Xml).Skip(1));
        }

        [Fact]
        public void Failed_refresh_preparation_preserves_existing_definition()
        {
            var tasks = new FakeTasks();
            var xml = TaskXmlBuilder.BuildRetry(@"C:\old.exe", DateTime.Now.AddHours(1), "fixture", DateTime.Now);
            tasks.Register(TaskNames.Retry, xml);
            var service = new ScheduleService(tasks, new ListLog(), Exe, () => DateTime.Now,
                path => throw new System.IO.IOException("fixture"));
            Assert.Throws<WinTool.Core.Updates.InstalledCopyRequiredException>(() => service.RefreshExisting());
            Assert.Equal(xml, Assert.Single(tasks.All).Xml);
        }

        [Fact]
        public void Run_once_releases_all_operation_scopes_before_starting_worker()
        {
            var tasks = new FakeTasks(); var held = 0;
            tasks.BeforeRun = () => Assert.Equal(0, held);
            var service = new ScheduleService(tasks, new ListLog(), Exe, () => DateTime.Now, path => path,
                retire: _ => Assert.True(held > 0), acquire: () => { held++; return new Release(() => held--); });
            service.StartRunOnce();
            Assert.Single(tasks.Started);
        }

        [Fact]
        public void Uninstall_waits_for_busy_gate_and_keeps_tasks_when_wait_expires()
        {
            var tasks = new FakeTasks(); tasks.Register(TaskNames.Retry, "fixture");
            var attempts = 0;
            var service = new ScheduleService(tasks, new ListLog(), Exe, () => DateTime.Now, path => path,
                acquire: () => { if (attempts++ == 0) throw new WinTool.Core.Operations.OperationBusyException(); return new Release(() => { }); });
            service.RemoveAllWaiting(TimeSpan.FromSeconds(1));
            Assert.Empty(tasks.All);
            tasks.Register(TaskNames.Retry, "fixture");
            var blocked = new ScheduleService(tasks, new ListLog(), Exe, () => DateTime.Now, path => path,
                acquire: () => throw new WinTool.Core.Operations.OperationBusyException());
            Assert.Throws<WinTool.Core.Operations.OperationBusyException>(() => blocked.RemoveAllWaiting(TimeSpan.Zero));
            Assert.Single(tasks.All);
        }

        private sealed class Release : IDisposable
        {
            private readonly Action action;
            public Release(Action action) { this.action = action; }
            public void Dispose() { action(); }
        }

        [Fact]
        public void All_registration_paths_refuse_a_portable_copy_before_modifying_existing_tasks()
        {
            var tasks = new FakeTasks();
            tasks.All.Add(new TaskEntry { Folder = TaskNames.Folder, Name = TaskNames.Main });
            var service = new ScheduleService(tasks, new ListLog(), Exe, () => DateTime.Now, path => throw new WinTool.Core.Updates.InstalledCopyRequiredException());
            Assert.Throws<WinTool.Core.Updates.InstalledCopyRequiredException>(() => service.Apply(new WindowsUpdateSettings { ScheduleEnabled = true }));
            Assert.Throws<WinTool.Core.Updates.InstalledCopyRequiredException>(() => service.ScheduleRetry(DateTime.Now, "fixture", 1));
            Assert.Throws<WinTool.Core.Updates.InstalledCopyRequiredException>(() => service.StartRunOnce());
            Assert.Single(tasks.All);
        }

        [Fact]
        public void Saving_replaces_every_own_task_in_any_folder()
        {
            var tasks = new FakeTasks();
            tasks.All.Add(new TaskEntry { Folder = @"\", Name = "WinTool by PcNinja" });
            tasks.All.Add(new TaskEntry { Folder = @"\Old", Name = "WinTool by PcNinja Retry" });
            tasks.All.Add(new TaskEntry { Folder = @"\", Name = "Some other vendor task" });

            Service(tasks).Apply(new WindowsUpdateSettings { ScheduleEnabled = true });

            var own = tasks.All.Where(t => TaskNames.IsOwn(t.Name)).ToList();
            var only = Assert.Single(own);
            Assert.Equal(TaskNames.Folder, only.Folder);
            Assert.Contains(tasks.All, t => t.Name == "Some other vendor task");
        }

        [Fact]
        public void Turning_the_schedule_off_removes_the_task()
        {
            var tasks = new FakeTasks();
            var service = Service(tasks);
            service.Apply(new WindowsUpdateSettings { ScheduleEnabled = true });
            service.Apply(new WindowsUpdateSettings { ScheduleEnabled = false });
            Assert.Empty(tasks.All);
        }

        [Fact]
        public void A_task_that_cannot_be_removed_stops_the_save()
        {
            var tasks = new FakeTasks();
            tasks.All.Add(new TaskEntry { Folder = @"\Locked", Name = "WinTool by PcNinja" });
            tasks.Undeletable.Add(@"\Locked|WinTool by PcNinja");

            Assert.Throws<InvalidOperationException>(() => Service(tasks).Apply(new WindowsUpdateSettings { ScheduleEnabled = true }));
            Assert.DoesNotContain(tasks.All, t => t.Folder == TaskNames.Folder);
        }

        [Fact]
        public void Legacy_winupdate_tasks_are_not_touched_by_a_normal_save()
        {
            var tasks = new FakeTasks();
            tasks.All.Add(new TaskEntry { Folder = TaskNames.Folder, Name = "PcNinja WinUpdate Tool" });
            Service(tasks).Apply(new WindowsUpdateSettings { ScheduleEnabled = true });
            Assert.Contains(tasks.All, t => TaskNames.IsLegacyWinUpdate(t.Name));
        }

        [Fact]
        public void Status_detects_a_task_that_no_longer_matches()
        {
            var tasks = new FakeTasks();
            var service = Service(tasks);
            var settings = new WindowsUpdateSettings { ScheduleEnabled = true, Frequency = "Daily", Time = "03:00" };
            service.Apply(settings);
            Assert.True(service.GetStatus(settings).MatchesSettings);

            settings.Time = "05:00";
            Assert.False(service.GetStatus(settings).MatchesSettings);

            tasks.All.Clear();
            settings.Time = "03:00";
            Assert.False(service.GetStatus(settings).MatchesSettings);
        }

        [Fact]
        public void Matching_ignores_how_task_scheduler_reformats_dates()
        {
            var settings = new WindowsUpdateSettings { ScheduleEnabled = true, Frequency = "Weekly", DayOfWeek = "Monday" };
            var service = Service(new FakeTasks());
            var registered = TaskXmlBuilder.BuildSchedule(settings, Exe, new DateTime(2025, 1, 2, 9, 0, 0))
                .Replace("T03:00:00<", "T03:00:00+02:00<");
            Assert.True(service.Matches(registered, settings));
        }

        [Fact]
        public void Matching_ignores_the_order_task_scheduler_stores_monthly_elements_in()
        {
            // Seen on Windows 11 23H2: the registered task lists Months before DaysOfMonth.
            var settings = new WindowsUpdateSettings { ScheduleEnabled = true, Frequency = "Monthly", MonthlyDay = 15 };
            var service = Service(new FakeTasks());
            var built = TaskXmlBuilder.BuildSchedule(settings, Exe, new DateTime(2026, 11, 6, 14, 23, 0));
            var doc = System.Xml.Linq.XDocument.Parse(built);
            System.Xml.Linq.XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            var byMonth = doc.Descendants(ns + "ScheduleByMonth").Single();
            var days = byMonth.Element(ns + "DaysOfMonth");
            days.Remove();
            byMonth.Add(days);
            Assert.True(service.Matches(doc.ToString(), settings), string.Join(" ", service.Compare(doc.ToString(), settings)));
        }

        [Fact]
        public void Retry_task_is_registered_and_cancelled()
        {
            var tasks = new FakeTasks();
            var service = Service(tasks);
            service.ScheduleRetry(new DateTime(2026, 11, 6, 10, 5, 0), "network", 1);
            Assert.NotNull(tasks.Get(TaskNames.Retry));
            service.CancelRetry();
            Assert.Null(tasks.Get(TaskNames.Retry));
        }

        [Fact]
        public void Run_once_registers_and_starts_the_task()
        {
            var tasks = new FakeTasks();
            Service(tasks).StartRunOnce();
            Assert.Equal(new[] { TaskNames.RunOnce }, tasks.Started);
        }

        [Fact]
        public void Description_reads_naturally()
        {
            Assert.Equal("weekly on Friday at 03:00, and at startup after 5 minute(s)",
                ScheduleService.Describe(new WindowsUpdateSettings { Frequency = "Weekly", DayOfWeek = "Friday", RunAtStartup = true }));
        }
    }
}
