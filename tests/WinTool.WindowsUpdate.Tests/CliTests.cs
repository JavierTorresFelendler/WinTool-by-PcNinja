using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using WinTool.Cli;
using WinTool.WindowsUpdate.Engine;
using WinTool.WindowsUpdate.Logic;
using WinTool.WindowsUpdate.Schedule;
using WinTool.WindowsUpdate.Storage;
using Xunit;

namespace WinTool.WindowsUpdate.Tests
{
    internal sealed class FakeEnvironment : IWindowsUpdateEnvironment, IDisposable
    {
        public FakeAgent Agent { get; } = new FakeAgent();
        public FakeProbe FakeProbe { get; } = new FakeProbe();
        public MemoryStore Memory { get; } = new MemoryStore();
        public FakeTasks FakeTasks { get; } = new FakeTasks();
        public ListLog ListLog { get; } = new ListLog();
        public List<string> ExtraLogSources { get; } = new List<string>();
        public string Legacy { get; set; }
        public bool Admin { get; set; } = true;
        public bool LockBusy { get; set; }
        public string Temp { get; } = Path.Combine(Path.GetTempPath(), "WinToolCliTests", Guid.NewGuid().ToString("N"));
        public HealthFacts Health { get; set; } = new HealthFacts();

        public IRunLog Log => ListLog;
        public IWindowsUpdateStore Store => Memory;
        public ISystemProbe Probe => FakeProbe;
        public ITaskSchedulerService Tasks => FakeTasks;
        public Func<DateTime> Clock => () => new DateTime(2026, 11, 6, 10, 0, 0);
        public bool IsAdministrator => Admin;
        public string WindowsVersion => "Windows 11 Pro 24H2 (build 26100.1)";
        public string DataFolder => Path.Combine(Temp, "Data");
        public string ExecutablePath => @"C:\Program Files\PcNinja\WinTool\WinTool-by-PcNinja.exe";
        public IUpdateAgent CreateAgent() => Agent;
        public IDisposable TryAcquireOperationLock() => LockBusy ? null : new Released();
        public WindowsUpdateReset CreateReset() => new WindowsUpdateReset(new OkServices(), new MemoryFolders(), Log, Path.Combine(Temp, "Windows"), Clock);
        public HealthFacts CollectHealthFacts(bool checkNetwork) => Health;
        public string ReadLegacyConfig() => Legacy;
        public FakeLegacyInstallations FakeLegacy { get; } = new FakeLegacyInstallations();
        public WinTool.WindowsUpdate.Migration.ILegacyInstallations LegacyInstallations => FakeLegacy;

        public IEnumerable<string> LogSources()
        {
            var logs = Path.Combine(Temp, "Logs");
            Directory.CreateDirectory(logs);
            File.WriteAllText(Path.Combine(logs, "WinTool-20261106.log"), "2026-11-06 10:00:00  INFO   App  started");
            yield return logs;
            foreach (var source in ExtraLogSources) yield return source;
        }

        public void Dispose()
        {
            try { Directory.Delete(Temp, true); } catch (IOException) { }
        }

        private sealed class Released : IDisposable { public void Dispose() { } }

        private sealed class OkServices : IServiceControl
        {
            public void Stop(string service) { }
            public void Start(string service) { }
            public bool IsRunning(string service) => true;
        }

        private sealed class MemoryFolders : IFolderOperations
        {
            private readonly HashSet<string> existing = new HashSet<string>();
            public bool Exists(string path) => existing.Contains(path);
            public void DeleteRecursive(string path) => existing.Remove(path);
            public void Create(string path) => existing.Add(path);
            public void Move(string from, string to) { existing.Remove(from); existing.Add(to); }
            public int CountEntries(string path) => 0;
        }
    }

    public class CliTests
    {
        [Fact]
        public void Log_collection_retains_distinct_old_and_new_logs_without_migration()
        {
            using (var env = new FakeEnvironment())
            {
                var legacy = Path.Combine(env.Temp, "Old", "Logs"); Directory.CreateDirectory(legacy);
                var old = Path.Combine(legacy, "WinTool-20261106.log"); File.WriteAllText(old, "legacy evidence"); env.ExtraLogSources.Add(legacy);
                var zip = Path.Combine(env.Temp, "collected.zip");
                Assert.Equal(0, Run(env, "logs", "collect", "--output", zip).code);
                using (var archive = ZipFile.OpenRead(zip))
                {
                    var logs = archive.Entries.Where(entry => entry.Name == "WinTool-20261106.log").ToList();
                    Assert.Equal(2, logs.Count);
                    Assert.Contains(logs, entry => { using (var reader = new StreamReader(entry.Open())) return reader.ReadToEnd() == "legacy evidence"; });
                }
                Assert.Equal("legacy evidence", File.ReadAllText(old));
            }
        }
        private static (int code, string output, string error) Run(FakeEnvironment env, params string[] args)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var code = new CommandRunner(env, output, error).Run(args);
            return (code, output.ToString(), error.ToString());
        }

        [Theory]
        [InlineData("update", "fly")]
        [InlineData("update", "scan", "--bogus")]
        [InlineData("update", "run", "--scheduled", "--retry")]
        [InlineData("update", "run", "--scheduled", "--snooze")]
        [InlineData("schedule", "set", "--enable", "--disable")]
        [InlineData("schedule", "set", "--month-day", "ten")]
        [InlineData("schedule", "set", "--wake", "--no-wake")]
        [InlineData("update", "reset")]
        public void Mistakes_are_usage_errors(params string[] args)
        {
            using (var env = new FakeEnvironment())
            {
                var result = Run(env, args);
                Assert.Equal(CliExit.Usage, result.code);
                Assert.NotEmpty(result.error);
            }
        }

        [Fact]
        public void Scan_lists_updates_and_counts()
        {
            using (var env = new FakeEnvironment())
            {
                env.Agent.Available.Add(U.Windows("2026-11 Cumulative Update"));
                env.Agent.Available.Add(U.Driver("Lenovo BIOS Update"));
                var result = Run(env, "update", "scan");
                Assert.Equal(0, result.code);
                Assert.Contains("Important: 1", result.output);
                Assert.Contains("[Firmware] Lenovo BIOS Update  - skipped: firmware", result.output);
            }
        }

        [Fact]
        public void Scan_does_not_repeat_a_kb_number_that_is_already_in_the_title()
        {
            using (var env = new FakeEnvironment())
            {
                var update = U.Windows("2025-10 Cumulative Update for .NET Framework (KB5066133)");
                update.KbArticles.Add("KB5066133");
                env.Agent.Available.Add(update);
                env.Agent.KbFor[update.Title] = "KB5066133";
                var result = Run(env, "update", "scan");
                Assert.Contains("(KB5066133)" + Environment.NewLine, result.output);
                Assert.DoesNotContain("(KB5066133) (KB5066133)", result.output);
            }
        }

        [Fact]
        public void Scan_json_is_machine_readable()
        {
            using (var env = new FakeEnvironment())
            {
                env.Agent.Available.Add(U.Optional("Preview"));
                var result = Run(env, "update", "scan", "--json", "--windows");
                Assert.Equal(0, result.code);
                Assert.Contains("\"scope\": \"Windows\"", result.output);
                Assert.Contains("\"excluded\": \"Scope\"", result.output);
            }
        }

        [Fact]
        public void Changing_commands_need_an_administrator()
        {
            using (var env = new FakeEnvironment { Admin = false })
            {
                Assert.Equal(CliExit.AdministratorRequired, Run(env, "update", "run").code);
                Assert.Equal(CliExit.AdministratorRequired, Run(env, "schedule", "set", "--enable").code);
                Assert.Equal(CliExit.AdministratorRequired, Run(env, "update", "reset", "--confirm").code);
                Assert.Equal(0, Run(env, "update", "scan").code);
            }
        }

        [Fact]
        public void Run_installs_and_reports_the_engine_exit_code()
        {
            using (var env = new FakeEnvironment())
            {
                env.Agent.Available.Add(U.Windows("KB update"));
                env.Agent.RebootAfterInstall = true;
                var result = Run(env, "update", "run");
                Assert.Equal(ExitCodes.RebootRequired, result.code);
                Assert.Contains("Downloading 1/1: KB update", result.output);
                Assert.Contains("Result: RebootRequired (exit code 10)", result.output);
            }
        }

        [Fact]
        public void Scheduled_run_with_no_network_schedules_a_retry_task()
        {
            using (var env = new FakeEnvironment())
            {
                env.FakeProbe.NetworkReady = false;
                var result = Run(env, "update", "run", "--scheduled", "--json");
                Assert.Equal(ExitCodes.RetryScheduled, result.code);
                Assert.NotNull(env.FakeTasks.Get(TaskNames.Retry));
                Assert.Contains("\"result\": \"RetryScheduled\"", result.output);
            }
        }

        [Fact]
        public void Schedule_set_saves_settings_and_registers_the_task()
        {
            using (var env = new FakeEnvironment())
            {
                var result = Run(env, "schedule", "set", "--enable", "--frequency", "weekly", "--day", "friday", "--time", "04:30", "--wake", "--no-run-if-missed");
                Assert.Equal(0, result.code);
                Assert.Contains("weekly on Friday at 04:30", result.output);
                var settings = env.Memory.Settings;
                Assert.True(settings.ScheduleEnabled);
                Assert.True(settings.WakeToRun);
                Assert.False(settings.RunIfMissed);
                Assert.NotNull(env.FakeTasks.Get(TaskNames.Main));
            }
        }

        [Fact]
        public void Schedule_set_accepts_the_msi_value_form()
        {
            using (var env = new FakeEnvironment())
            {
                var result = Run(env, "schedule", "set", "--enable", "--frequency=Weekly", "--day=Friday", "--time=04:30",
                    "--wake=1", "--run-if-missed=0", "--at-startup=", "--firmware=0", "--reboot-prompt=1", "--auto-retry=1", "--retry-delay=5");
                Assert.Equal(0, result.code);
                var s = env.Memory.Settings;
                Assert.True(s.WakeToRun);
                Assert.False(s.RunIfMissed);
                Assert.False(s.RunAtStartup);
                Assert.Equal("Friday", s.DayOfWeek);
                Assert.Equal(CliExit.Usage, Run(env, "schedule", "set", "--wake=maybe").code);
            }
        }

        [Fact]
        public void Invalid_schedule_changes_nothing()
        {
            using (var env = new FakeEnvironment())
            {
                var result = Run(env, "schedule", "set", "--enable", "--time", "25:99");
                Assert.Equal(CliExit.Usage, result.code);
                Assert.False(env.Memory.Settings.ScheduleEnabled);
                Assert.Empty(env.FakeTasks.All);
            }
        }

        [Fact]
        public void A_task_that_cannot_be_replaced_leaves_the_settings_unchanged()
        {
            using (var env = new FakeEnvironment())
            {
                env.FakeTasks.All.Add(new TaskEntry { Folder = @"\Locked", Name = TaskNames.Main });
                env.FakeTasks.Undeletable.Add(@"\Locked|" + TaskNames.Main);
                var result = Run(env, "schedule", "set", "--enable");
                Assert.Equal(CliExit.Failed, result.code);
                Assert.False(env.Memory.Settings.ScheduleEnabled);
            }
        }

        [Fact]
        public void Schedule_remove_turns_it_off()
        {
            using (var env = new FakeEnvironment())
            {
                Run(env, "schedule", "set", "--enable");
                Assert.Equal(0, Run(env, "schedule", "remove").code);
                Assert.False(env.Memory.Settings.ScheduleEnabled);
                Assert.Empty(env.FakeTasks.All);
            }
        }

        [Fact]
        public void Schedule_import_reads_wintool_and_v2_files()
        {
            using (var env = new FakeEnvironment())
            {
                Directory.CreateDirectory(env.Temp);
                var own = Path.Combine(env.Temp, "own.json");
                File.WriteAllText(own, WinTool.Core.IO.JsonFileStore<WindowsUpdateSettings>.Serialize(new WindowsUpdateSettings { ScheduleEnabled = true, Frequency = "Daily" }));
                Assert.Equal(0, Run(env, "schedule", "import", own).code);
                Assert.Equal("Daily", env.Memory.Settings.Frequency);

                var v2 = Path.Combine(env.Temp, "config.json");
                File.WriteAllText(v2, "{ \"Enabled\": true, \"Frequency\": \"Weekly\", \"DayOfWeek\": \"Monday\" }");
                Assert.Equal(0, Run(env, "schedule", "import", v2).code);
                Assert.Equal("Monday", env.Memory.Settings.DayOfWeek);
            }
        }

        [Fact]
        public void Import_v2_takes_over_the_old_tasks()
        {
            using (var env = new FakeEnvironment())
            {
                env.Legacy = "{ \"Enabled\": true, \"Frequency\": \"Monthly\", \"MonthlyDay\": 10, \"Time\": \"02:00\" }";
                env.FakeTasks.All.Add(new TaskEntry { Folder = @"\PcNinja", Name = "PcNinja WinUpdate Tool" });
                env.FakeTasks.All.Add(new TaskEntry { Folder = @"\PcNinja", Name = "PcNinja WinUpdate Tool Retry" });

                var result = Run(env, "schedule", "import-v2");

                Assert.Equal(0, result.code);
                Assert.DoesNotContain(env.FakeTasks.All, t => TaskNames.IsLegacyWinUpdate(t.Name));
                Assert.NotNull(env.FakeTasks.Get(TaskNames.Main));
                Assert.Equal(10, env.Memory.Settings.MonthlyDay);
                Assert.Contains("took over", result.output);
            }
        }

        [Fact]
        public void Import_v2_without_v2_fails_clearly()
        {
            using (var env = new FakeEnvironment())
            {
                var result = Run(env, "schedule", "import-v2");
                Assert.Equal(CliExit.Failed, result.code);
                Assert.Contains("No WinUpdate Tool was found", result.error);
            }
        }

        [Fact]
        public void If_present_with_nothing_to_migrate_succeeds_quietly()
        {
            using (var env = new FakeEnvironment())
            {
                Assert.Equal(CliExit.Ok, Run(env, "schedule", "import-v2", "--if-present").code);
            }
        }

        [Fact]
        public void Import_v2_uninstalls_the_old_tool_after_taking_its_schedule()
        {
            using (var env = new FakeEnvironment())
            {
                env.Legacy = "{ \"Enabled\": true, \"Frequency\": \"Weekly\", \"DayOfWeek\": \"Sunday\", \"Time\": \"04:00\" }";
                env.FakeLegacy.Installed.Add(WinTool.WindowsUpdate.Migration.LegacyInstallPolicy.ForMsi("{AAAAAAAA-0000-0000-0000-000000000001}"));
                var result = Run(env, "schedule", "import-v2", "--json");
                Assert.Equal(CliExit.Ok, result.code);
                Assert.Equal(new[] { "{AAAAAAAA-0000-0000-0000-000000000001}" }, env.FakeLegacy.Uninstalled);
                Assert.NotNull(env.FakeTasks.Get(TaskNames.Main));
                Assert.Contains("\"result\": \"Migrated\"", result.output);
                // The MSI runs this without a console: the log must record what happened.
                Assert.True(env.ListLog.Contains("WinUpdate Tool takeover: found its settings file, 0 scheduled task(s) and 1 installation(s)"));
                Assert.True(env.ListLog.Contains("WinUpdate Tool takeover: importing its schedule: "));
                Assert.True(env.ListLog.Contains("WinUpdate Tool takeover: uninstalled "));
                Assert.True(env.ListLog.Contains("WinUpdate Tool takeover: completed: 0 task(s) removed, 1 installation(s) uninstalled."));
                Assert.Equal(TakeoverNotices.Imported, env.Store.LoadState().PendingTakeoverNotice);
            }
        }

        [Fact]
        public void Keep_schedule_is_logged_and_the_old_schedule_is_not_imported()
        {
            using (var env = new FakeEnvironment())
            {
                env.Legacy = "{ \"Enabled\": true, \"Frequency\": \"Weekly\", \"DayOfWeek\": \"Sunday\", \"Time\": \"04:00\" }";
                env.FakeLegacy.Installed.Add(WinTool.WindowsUpdate.Migration.LegacyInstallPolicy.ForMsi("{AAAAAAAA-0000-0000-0000-000000000004}"));
                Assert.Equal(CliExit.Ok, Run(env, "schedule", "import-v2", "--keep-schedule", "--if-present").code);
                Assert.Null(env.FakeTasks.Get(TaskNames.Main));
                Assert.True(env.ListLog.Contains("keeping the schedule chosen in the WinTool installer"));
                Assert.Equal(TakeoverNotices.Kept, env.Store.LoadState().PendingTakeoverNotice);
            }
        }

        [Fact]
        public void Nothing_to_migrate_is_logged()
        {
            using (var env = new FakeEnvironment())
            {
                Assert.Equal(CliExit.Ok, Run(env, "schedule", "import-v2", "--if-present").code);
                Assert.True(env.ListLog.Contains("WinUpdate Tool takeover: nothing to migrate"));
                Assert.Null(env.Store.LoadState().PendingTakeoverNotice);
            }
        }

        [Fact]
        public void An_installed_old_tool_without_settings_or_tasks_is_still_found_and_removed()
        {
            using (var env = new FakeEnvironment())
            {
                env.FakeLegacy.Installed.Add(WinTool.WindowsUpdate.Migration.LegacyInstallPolicy.ForMsi("{AAAAAAAA-0000-0000-0000-000000000002}"));
                Assert.Equal(CliExit.Ok, Run(env, "schedule", "import-v2").code);
                Assert.Single(env.FakeLegacy.Uninstalled);
                Assert.Null(env.FakeTasks.Get(TaskNames.Main));
            }
        }

        [Fact]
        public void A_failed_uninstall_is_reported_and_not_called_a_completed_migration()
        {
            using (var env = new FakeEnvironment())
            {
                env.FakeLegacy.Installed.Add(WinTool.WindowsUpdate.Migration.LegacyInstallPolicy.ForMsi("{AAAAAAAA-0000-0000-0000-000000000003}"));
                env.FakeLegacy.ExitCode = 1603;
                var result = Run(env, "schedule", "import-v2", "--json");
                Assert.Equal(CliExit.Failed, result.code);
                Assert.Contains("\"result\": \"Incomplete\"", result.output);
                Assert.Contains("1603", result.output);
                Assert.True(env.ListLog.Contains("WinUpdate Tool takeover: incomplete: 1 installation(s) could not be uninstalled."));
                Assert.Null(env.Store.LoadState().PendingTakeoverNotice);
            }
        }

        [Fact]
        public void Reset_needs_confirmation_and_the_operation_lock()
        {
            using (var env = new FakeEnvironment())
            {
                Assert.Equal(0, Run(env, "update", "reset", "--confirm").code);
                env.LockBusy = true;
                Assert.Equal(CliExit.Busy, Run(env, "update", "reset", "--confirm").code);
            }
        }

        [Fact]
        public void Health_exit_code_follows_the_worst_item()
        {
            using (var env = new FakeEnvironment())
            {
                Assert.Equal(CliExit.Ok, Run(env, "update", "health").code);
                env.Health = new HealthFacts { PendingRestart = FakeProbe.PendingNow() };
                Assert.Equal(CliExit.Warning, Run(env, "update", "health").code);
                env.Health = new HealthFacts { SystemDriveFreeBytes = 1024 };
                Assert.Equal(CliExit.Failed, Run(env, "update", "health", "--json").code);
            }
        }

        [Fact]
        public void Health_reports_a_schedule_that_no_longer_matches()
        {
            using (var env = new FakeEnvironment())
            {
                Run(env, "schedule", "set", "--enable");
                env.FakeTasks.All.Clear();
                var result = Run(env, "update", "health");
                Assert.Equal(CliExit.Failed, result.code);
                Assert.Contains("scheduled task is missing", result.output);
            }
        }

        [Fact]
        public void Status_shows_the_last_run()
        {
            using (var env = new FakeEnvironment())
            {
                env.Memory.State.LastRunFinished = "2026-11-05T03:12:00";
                env.Memory.State.LastResult = "Succeeded";
                env.Memory.State.LastRunType = "Scheduled";
                var text = Run(env, "status");
                Assert.Equal(0, text.code);
                Assert.Contains("Last update run: 2026-11-05T03:12:00 (Scheduled, Succeeded)", text.output);
                var json = Run(env, "status", "--json");
                Assert.Contains("\"windows\": \"Windows 11 Pro 24H2 (build 26100.1)\"", json.output);
            }
        }

        [Fact]
        public void Logs_collect_creates_a_zip_with_logs_tasks_and_status()
        {
            using (var env = new FakeEnvironment())
            {
                Run(env, "schedule", "set", "--enable");
                var zip = Path.Combine(env.Temp, "out", "package.zip");
                var result = Run(env, "logs", "collect", "--output", zip);
                Assert.Equal(0, result.code);
                using (var archive = ZipFile.OpenRead(zip))
                {
                    var names = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
                    Assert.Contains("status.json", names);
                    Assert.Contains(names, n => n.EndsWith("WinTool-20261106.log"));
                    Assert.Contains(names, n => n.StartsWith("Tasks/") && n.EndsWith(TaskNames.Main + ".xml"));
                }
            }
        }

        [Fact]
        public void Start_as_system_starts_the_run_once_task()
        {
            using (var env = new FakeEnvironment())
            {
                Assert.Equal(0, Run(env, "update", "start-as-system").code);
                Assert.Equal(new[] { TaskNames.RunOnce }, env.FakeTasks.Started);
            }
        }
    }
}
