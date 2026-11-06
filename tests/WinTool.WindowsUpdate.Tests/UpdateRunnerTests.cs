using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WinTool.WindowsUpdate.Engine;
using WinTool.WindowsUpdate.Logic;
using WinTool.WindowsUpdate.Model;
using Xunit;

namespace WinTool.WindowsUpdate.Tests
{
    public class UpdateRunnerTests
    {
        [Fact]
        public void No_updates_ends_with_NoUpdates_and_records_success()
        {
            var h = new Harness();
            var outcome = h.Run();
            Assert.Equal(RunResult.NoUpdates, outcome.Result);
            Assert.Equal("NoUpdates", h.Store.State.LastResult);
            Assert.NotNull(h.Store.State.LastSuccessfulRun);
            Assert.Equal(ExitCodes.Ok, ExitCodes.For(outcome.Result));
        }

        [Fact]
        public void Installs_found_updates_then_stops_when_nothing_remains()
        {
            var h = new Harness();
            h.Agent.Available.Add(U.Windows("2026-11 Cumulative Update"));
            h.Agent.Available.Add(U.Optional());
            h.Agent.Available.Add(U.Driver());

            var outcome = h.Run();

            Assert.Equal(RunResult.Succeeded, outcome.Result);
            Assert.Equal(3, h.Agent.Installed.Count);
            Assert.Equal(1, outcome.PassesCompleted);
            Assert.Equal(3, outcome.UpdatesFound);
        }

        [Fact]
        public void A_second_pass_installs_updates_that_appear_after_the_first()
        {
            var h = new Harness();
            h.Agent.Available.Add(U.Windows("Servicing stack"));
            h.Agent.AppearAfterInstall.Enqueue(new List<UpdateItem> { U.Windows("Cumulative after servicing stack") });

            var outcome = h.Run();

            Assert.Equal(RunResult.Succeeded, outcome.Result);
            Assert.Equal(2, outcome.PassesCompleted);
            Assert.Equal(new[] { "Servicing stack", "Cumulative after servicing stack" }, h.Agent.Installed);
        }

        [Fact]
        public void Stops_after_the_maximum_number_of_passes()
        {
            var h = new Harness();
            h.Agent.Available.Add(U.Windows("1"));
            for (var i = 0; i < 5; i++) h.Agent.AppearAfterInstall.Enqueue(new List<UpdateItem> { U.Windows("more " + i) });

            var outcome = h.Run();

            Assert.Equal(RunResult.MaxPassesReached, outcome.Result);
            Assert.Equal(3, outcome.PassesCompleted);
        }

        [Fact]
        public void Restart_required_after_install_ends_the_run()
        {
            var h = new Harness();
            h.Agent.Available.Add(U.Windows());
            h.Agent.RebootAfterInstall = true;

            var outcome = h.Run();

            Assert.Equal(RunResult.RebootRequired, outcome.Result);
            Assert.True(outcome.RebootRequired);
            Assert.True(h.Store.State.LastRebootRequired);
            Assert.Equal(ExitCodes.RebootRequired, ExitCodes.For(outcome.Result));
        }

        [Fact]
        public void A_pending_restart_stops_the_run_before_searching()
        {
            var h = new Harness();
            h.Probe.Pending = FakeProbe.PendingNow();
            h.Agent.Available.Add(U.Windows());

            var outcome = h.Run();

            Assert.Equal(RunResult.RebootRequired, outcome.Result);
            Assert.Equal(0, h.Agent.Searches);
        }

        [Fact]
        public void Scheduled_run_respects_the_cooldown_but_manual_does_not()
        {
            var h = new Harness();
            h.Store.State.LastRunFinished = WinTool.WindowsUpdate.Storage.WindowsUpdateState.Stamp(h.Now.AddMinutes(-2));

            Assert.Equal(RunResult.CooldownActive, h.Run(RunType.Scheduled).Result);
            Assert.Equal(0, h.Agent.Searches);

            h.Store.State.LastRunFinished = WinTool.WindowsUpdate.Storage.WindowsUpdateState.Stamp(h.Now.AddMinutes(-2));
            Assert.Equal(RunResult.NoUpdates, h.Run(RunType.Manual).Result);
        }

        [Fact]
        public void Network_not_ready_schedules_a_retry_for_scheduled_runs()
        {
            var h = new Harness { };
            h.Probe.NetworkReady = false;

            var outcome = h.Run(RunType.Scheduled);

            Assert.Equal(RunResult.RetryScheduled, outcome.Result);
            var retry = Assert.Single(h.Retries.Scheduled);
            Assert.Equal(h.Now.AddMinutes(5), retry.Item1);
            Assert.Equal(1, retry.Item3);
            Assert.Equal(1, h.Store.State.RetryCount);
            Assert.Equal(ExitCodes.RetryScheduled, ExitCodes.For(outcome.Result));
        }

        [Fact]
        public void Network_not_ready_in_the_window_reports_without_scheduling()
        {
            var h = new Harness();
            h.Probe.NetworkReady = false;

            var outcome = h.Run(RunType.Manual, interactive: true);

            Assert.Equal(RunResult.Failed, outcome.Result);
            Assert.Empty(h.Retries.Scheduled);
            Assert.Contains("network", outcome.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Retry_delay_doubles_on_each_retry()
        {
            var h = new Harness();
            h.Probe.NetworkReady = false;
            h.Store.State.RetryCount = 2;

            h.Run(RunType.Retry);

            var retry = Assert.Single(h.Retries.Scheduled);
            Assert.Equal(3, retry.Item3);
            Assert.Equal(h.Now.AddMinutes(20), retry.Item1);
        }

        [Fact]
        public void Retry_limit_and_disabled_retry_need_attention()
        {
            var h = new Harness();
            h.Probe.NetworkReady = false;
            h.Store.State.RetryCount = 3;
            Assert.Equal(RunResult.MaxRetriesReached, h.Run(RunType.Retry).Result);

            var off = new Harness();
            off.Probe.NetworkReady = false;
            off.Store.Settings.AutoRetryEnabled = false;
            var outcome = off.Run(RunType.Scheduled);
            Assert.Equal(RunResult.RetryDisabled, outcome.Result);
            Assert.Equal(ExitCodes.NeedsAttention, ExitCodes.For(outcome.Result));
            Assert.Empty(off.Retries.Scheduled);
        }

        [Fact]
        public void A_new_non_retry_run_clears_the_retry_counter()
        {
            var h = new Harness();
            h.Store.State.RetryCount = 2;
            h.Store.State.LastRetryScheduled = "2026-11-06T11:00:00";

            h.Run(RunType.Scheduled);

            Assert.Equal(0, h.Store.State.RetryCount);
            Assert.Null(h.Store.State.LastRetryScheduled);
            Assert.True(h.Retries.Cancels > 0);
        }

        [Fact]
        public void Windows_busy_schedules_a_retry_for_automatic_runs()
        {
            var h = new Harness();
            h.Probe.Activity = ActivityEvaluator.Evaluate(new[] { "TiWorker" }, false);
            h.Agent.Available.Add(U.Windows());

            var outcome = h.Run(RunType.Scheduled);

            Assert.Equal(RunResult.RetryScheduled, outcome.Result);
            Assert.Empty(h.Agent.Installed);
            Assert.Equal(0, h.Probe.ServicesStopped);
        }

        [Fact]
        public void Manual_run_can_snooze_windows_update_and_continue()
        {
            var h = new Harness();
            h.Probe.Activity = ActivityEvaluator.Evaluate(new[] { "MoUsoCoreWorker" }, false);
            h.Agent.Available.Add(U.Windows());

            var outcome = h.Run(RunType.Manual, configure: r => r.OnWindowsBusy = a => BusyDecision.Snooze);

            Assert.Equal(RunResult.Succeeded, outcome.Result);
            Assert.Equal(1, h.Probe.ServicesStopped);
        }

        [Fact]
        public void Manual_run_can_be_cancelled_when_windows_is_busy()
        {
            var h = new Harness();
            h.Probe.Activity = ActivityEvaluator.Evaluate(new[] { "TiWorker" }, false);

            var outcome = h.Run(RunType.Manual, configure: r => r.OnWindowsBusy = a => BusyDecision.Cancel);

            Assert.Equal(RunResult.Cancelled, outcome.Result);
            Assert.Equal(0, h.Agent.Searches);
        }

        [Fact]
        public void Firmware_is_skipped_unless_enabled()
        {
            var h = new Harness();
            h.Agent.Available.Add(U.Driver("Dell - Firmware - 1.25.0", "Firmware"));
            var skipped = h.Run();
            Assert.Equal(RunResult.NoUpdates, skipped.Result);
            Assert.Contains("firmware", skipped.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(h.Agent.Installed);

            h.Store.Settings.InstallFirmwareUpdates = true;
            Assert.Equal(RunResult.Succeeded, h.Run().Result);
            Assert.Single(h.Agent.Installed);
        }

        [Fact]
        public void Only_selected_updates_are_installed_in_one_pass()
        {
            var h = new Harness();
            var wanted = U.Windows("Wanted");
            h.Agent.Available.Add(wanted);
            h.Agent.Available.Add(U.Windows("Not wanted"));

            var outcome = h.Run(configure: r => r.SelectedKeys = new HashSet<string> { wanted.Key });

            Assert.Equal(RunResult.Succeeded, outcome.Result);
            Assert.Equal(new[] { "Wanted" }, h.Agent.Installed);
            Assert.Equal(1, outcome.PassesCompleted);
        }

        [Fact]
        public void A_failed_download_is_reported_per_update_and_the_rest_is_installed()
        {
            var h = new Harness();
            h.Agent.Available.Add(U.Windows("Good"));
            h.Agent.Available.Add(U.Windows("Broken"));
            h.Agent.DownloadFailures["Broken"] = unchecked((int)0x80246007);

            var outcome = h.Run();

            Assert.Equal(RunResult.SucceededWithErrors, outcome.Result);
            var failed = Assert.Single(outcome.Items, i => !i.Succeeded);
            Assert.Equal("Broken", failed.Update.Title);
            Assert.Equal(ExitCodes.CompletedWithErrors, ExitCodes.For(outcome.Result));
        }

        [Fact]
        public void Downloads_failing_on_a_network_error_are_retried()
        {
            var h = new Harness();
            h.Agent.Available.Add(U.Windows("A"));
            h.Agent.DownloadFailures["A"] = unchecked((int)0x80072EE2);

            Assert.Equal(RunResult.RetryScheduled, h.Run(RunType.Scheduled).Result);
            Assert.Single(h.Retries.Scheduled);
        }

        [Fact]
        public void Install_failure_ends_the_run_as_failed_with_details()
        {
            var h = new Harness();
            h.Agent.Available.Add(U.Windows("Bad"));
            h.Agent.InstallFailures.Add("Bad");

            var outcome = h.Run();

            Assert.Equal(RunResult.Failed, outcome.Result);
            Assert.Equal(unchecked((int)0x80070643), outcome.Items.Single().HResult);
            Assert.True(h.Log.Contains("0x80070643"));
        }

        [Fact]
        public void Retryable_search_errors_schedule_a_retry_and_others_fail()
        {
            var h = new Harness();
            h.Agent.SearchErrorHResult = unchecked((int)0x8024402C);
            Assert.Equal(RunResult.RetryScheduled, h.Run(RunType.Scheduled).Result);

            var other = new Harness();
            other.Agent.SearchErrorHResult = unchecked((int)0x8024000E);
            var outcome = other.Run(RunType.Scheduled);
            Assert.Equal(RunResult.Failed, outcome.Result);
            Assert.Equal("Failed", other.Store.State.LastResult);
            Assert.Contains("data store", outcome.Message);
        }

        [Fact]
        public void A_busy_operation_lock_leaves_state_untouched()
        {
            var h = new Harness { LockBusy = true };
            var outcome = h.Run();
            Assert.Equal(RunResult.AlreadyRunning, outcome.Result);
            Assert.Equal(0, h.Store.StateSaves);
            Assert.Equal(ExitCodes.Skipped, ExitCodes.For(outcome.Result));
        }

        [Fact]
        public void Cancel_during_download_installs_nothing()
        {
            var h = new Harness();
            h.Agent.Available.Add(U.Windows("A"));
            h.Agent.Available.Add(U.Windows("B"));
            var cts = new CancellationTokenSource();
            h.Agent.OnDownload = () => cts.Cancel();

            var outcome = h.Run(cancellation: cts.Token);

            Assert.Equal(RunResult.Cancelled, outcome.Result);
            Assert.Empty(h.Agent.Installed);
        }

        [Fact]
        public void Licenses_are_accepted_before_download()
        {
            var h = new Harness();
            var update = U.Windows();
            h.Agent.Available.Add(update);
            h.Run();
            Assert.Contains(update.Key, h.Agent.EulasAccepted);
        }

        [Fact]
        public void Hebrew_titles_reach_the_log_unchanged()
        {
            var h = new Harness();
            h.Agent.Available.Add(U.Windows("עדכון מצטבר עבור Windows 11"));
            h.Run();
            Assert.True(h.Log.Contains("עדכון מצטבר עבור Windows 11"));
        }

        [Fact]
        public void Progress_reports_each_download()
        {
            var h = new Harness();
            h.Agent.Available.Add(U.Windows("A"));
            h.Agent.Available.Add(U.Windows("B"));
            var reports = new List<RunProgress>();
            h.Runner.Run(new RunRequest(), new SyncProgress(reports.Add));
            var downloads = reports.Where(r => r.Stage == RunStage.Downloading).ToList();
            Assert.Equal(2, downloads.Count);
            Assert.Equal(2, downloads.Last().Total);
        }

        private sealed class SyncProgress : IProgress<RunProgress>
        {
            private readonly Action<RunProgress> report;
            public SyncProgress(Action<RunProgress> report) { this.report = report; }
            public void Report(RunProgress value) => report(value);
        }
    }
}
