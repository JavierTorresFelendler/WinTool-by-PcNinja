using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WinTool.WindowsUpdate.Engine;
using WinTool.WindowsUpdate.Model;
using Xunit;

namespace WinTool.WindowsUpdate.Tests
{
    public class SelectedSearchTests
    {
        private sealed class ObservedAgent : IUpdateAgent, IObservedSearchAgent
        {
            public readonly FakeAgent Inner = new FakeAgent();
            public readonly List<string> Criteria = new List<string>();
            public readonly List<UpdateItem> Downloaded = new List<UpdateItem>();
            public SearchContext Context = Known();
            public bool Split;
            public int Registrations;
            public Action<int> AfterSearch;
            public IPreparedUpdateSearch PrepareSearch() => new Prepared(this);
            private sealed class Prepared : IPreparedUpdateSearch
            {
                private readonly ObservedAgent agent;
                public Prepared(ObservedAgent agent) { this.agent = agent; Context = agent.Context; }
                public SearchContext Context { get; }
                public SearchContext ReadContext() => agent.Context;
                public IReadOnlyList<UpdateItem> Search(string criteria) => agent.Search(criteria);
            }
            public bool EnsureMicrosoftUpdateRegistered() { Registrations++; return true; }
            public IReadOnlyList<UpdateItem> Search(string criteria)
            {
                Criteria.Add(criteria);
                var result = Inner.Search(criteria);
                if (Split && criteria == ScanService.BroadCriteria) result = result.Where(u => !u.IsDriverType && !u.BrowseOnly).ToList();
                AfterSearch?.Invoke(Criteria.Count);
                return result;
            }
            public void AcceptEula(UpdateItem u) => Inner.AcceptEula(u);
            public ItemResult Download(UpdateItem u, CancellationToken c) { Downloaded.Add(u); return Inner.Download(u, c); }
            public InstallOutcome Install(IReadOnlyList<UpdateItem> u) => Inner.Install(u);
        }
        private static SearchContext Known(bool registered = true) => new SearchContext(0, "", true, true, registered);
        private sealed class Scenario
        {
            public readonly ObservedAgent Agent = new ObservedAgent();
            public readonly FakeProbe Probe = new FakeProbe();
            public readonly MemoryStore Store = new MemoryStore();
            public readonly FakeRetries Retries = new FakeRetries();
            public readonly ListLog Log = new ListLog();
            public readonly UpdateScope Scope = UpdateScope.Default();
            public ScanResult Discovery;
            public RunRequest Request;
            public void Select(params UpdateItem[] selected)
            {
                Discovery = new ScanService(Agent, Log).Scan(Scope);
                var keys = new HashSet<string>(selected.Select(u => u.Key));
                Request = new RunRequest { SelectionWorkflow = true, SelectedKeys = keys, Selection = Discovery.Select(keys), Scope = Scope };
            }
            public RunOutcome Run(CancellationToken token = default(CancellationToken)) => new UpdateRunner(Agent, Probe, Store, Retries, Log,
                () => new DateTime(2026, 11, 13), () => new Lease()).Run(Request, cancellation: token, interactive: true);
        }
        private sealed class Lease : IDisposable { public void Dispose() { } }

        [Fact]
        public void Broad_selection_uses_seven_calls_fresh_objects_and_shared_snapshot()
        {
            var s = new Scenario(); var update = U.Windows(); s.Agent.Inner.Available.Add(update); s.Select(update);
            var old = s.Discovery.IncludedUpdates.Single();
            var result = s.Run();
            Assert.Equal(RunResult.Succeeded, result.Result);
            Assert.Equal(7, s.Agent.Criteria.Count);
            Assert.NotSame(old, s.Agent.Downloaded.Single());
            Assert.True(result.PostScan.Complete);
            Assert.False(result.VerificationUnknown);
            Assert.Empty(result.PostScan.IncludedUpdates);
            Assert.True(s.Log.Contains("sharedQuery="));
        }
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Reboot_keeps_one_verification_and_never_exceeds_seven(bool fallback)
        {
            var s = new Scenario(); var u = U.Windows(); s.Agent.Inner.Available.Add(u); s.Select(u);
            if (fallback) s.Agent.Context = SearchContext.Unknown;
            s.Agent.Inner.RebootAfterInstall = true;
            var result = s.Run();
            Assert.Equal(RunResult.RebootRequired, result.Result);
            Assert.Equal(fallback ? 7 : 5, s.Agent.Criteria.Count);
            Assert.Null(result.PostScan);
        }
        [Fact]
        public void Mixed_primary_sources_use_nine_calls()
        {
            var s = new Scenario(); s.Agent.Split = true;
            var items = new[] { U.Windows(), U.Driver(), U.Optional() }; s.Agent.Inner.Available.AddRange(items); s.Select(items);
            Assert.Equal(RunResult.Succeeded, s.Run().Result);
            Assert.Equal(9, s.Agent.Criteria.Count);
        }
        [Fact]
        public void Driver_seen_by_broad_and_driver_uses_primary_broad_only()
        {
            var s = new Scenario(); var u = U.Driver(); s.Agent.Inner.Available.Add(u); s.Select(u);
            Assert.Equal(new[] { "Broad", "Driver" }, s.Discovery.IncludedUpdates.Single().Sources);
            Assert.Equal("Broad", s.Discovery.IncludedUpdates.Single().PrimarySource);
            s.Run(); Assert.Equal(7, s.Agent.Criteria.Count);
        }
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Unknown_or_changed_context_falls_back_within_nine_calls(bool unknown)
        {
            var s = new Scenario(); var u = U.Windows(); s.Agent.Inner.Available.Add(u); s.Select(u);
            s.Agent.Context = unknown ? SearchContext.Unknown : Known(false);
            Assert.Equal(RunResult.Succeeded, s.Run().Result);
            Assert.Equal(9, s.Agent.Criteria.Count);
        }
        [Fact]
        public void Registration_changes_mid_query_complete_missing_classes_without_repeating()
        {
            var s = new Scenario(); var u = U.Windows(); s.Agent.Inner.Available.Add(u); s.Select(u);
            s.Agent.AfterSearch = n => { if (n == 4) s.Agent.Context = Known(false); };
            Assert.Equal(RunResult.Succeeded, s.Run().Result);
            Assert.Equal(9, s.Agent.Criteria.Count);
            Assert.Equal(new[] { ScanService.BroadCriteria, ScanService.DriverCriteria, ScanService.OptionalCriteria }, s.Agent.Criteria.Skip(3).Take(3));
        }
        [Fact]
        public void Fallback_after_driver_retains_canonical_merge_without_fourth_call()
        {
            var s = new Scenario(); s.Agent.Split = true; var u = U.Driver(); s.Agent.Inner.Available.Add(u); s.Select(u);
            s.Agent.Split = false;
            s.Agent.AfterSearch = n => { if (n == 4) s.Agent.Context = Known(false); };
            var result = s.Run();
            Assert.Equal(RunResult.Succeeded, result.Result); Assert.Equal(9, s.Agent.Criteria.Count);
            Assert.Equal("Broad", s.Agent.Downloaded.Single().PrimarySource);
            Assert.Equal(3, s.Agent.Criteria.Skip(3).Take(3).Distinct().Count());
        }
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Missing_or_changed_revision_skips_without_install_or_postscan(bool changed)
        {
            var s = new Scenario(); var u = U.Windows(); s.Agent.Inner.Available.Add(u); s.Select(u);
            s.Agent.Inner.Available.Clear();
            if (changed) s.Agent.Inner.Available.Add(new UpdateItem(u.UpdateId, u.Revision + 1, u.Title));
            var result = s.Run();
            Assert.Equal(4, s.Agent.Criteria.Count); Assert.Empty(s.Agent.Downloaded); Assert.Null(result.PostScan);
            Assert.Equal(RunResult.SelectionUnavailable, result.Result); Assert.Null(s.Store.State.LastSuccessfulRun);
            Assert.Equal(changed ? SelectionSkipReason.ChangedRevision : SelectionSkipReason.NoLongerOffered, result.SelectionSkipped.Single().Reason);
        }
        [Fact]
        public void Required_query_failure_fails_whole_operation_before_download()
        {
            var s = new Scenario(); var u = U.Windows(); s.Agent.Inner.Available.Add(u); s.Select(u);
            s.Agent.Inner.FailingCriteria.Add(ScanService.BroadCriteria);
            Assert.Equal(RunResult.Failed, s.Run().Result); Assert.Empty(s.Agent.Downloaded); Assert.Equal(4, s.Agent.Criteria.Count);
        }
        [Fact]
        public void Fallback_identity_does_not_silently_install_a_changed_revision()
        {
            var s = new Scenario(); var u = new UpdateItem("", 1, "Fallback title"); s.Agent.Inner.Available.Add(u); s.Select(u);
            s.Agent.Inner.Available.Clear(); s.Agent.Inner.Available.Add(new UpdateItem("", 2, u.Title));
            var result = s.Run(); Assert.Empty(s.Agent.Downloaded);
            Assert.Equal(SelectionSkipReason.ChangedRevision, result.SelectionSkipped.Single().Reason);
            Assert.Equal(RunResult.SelectionUnavailable, result.Result);
        }
        [Fact]
        public void Initial_context_change_is_unknown_for_reduced_planning()
        {
            var s = new Scenario(); var u = U.Windows(); s.Agent.Inner.Available.Add(u);
            s.Agent.AfterSearch = n => { if (n == 1) s.Agent.Context = Known(false); };
            s.Select(u); s.Agent.AfterSearch = null;
            Assert.False(s.Request.Selection.Entries.Single().Context.Known);
            Assert.Equal(RunResult.Succeeded, s.Run().Result); Assert.Equal(9, s.Agent.Criteria.Count);
        }
        [Fact]
        public void Cancellation_in_revalidation_stops_before_download_and_no_extra_queries()
        {
            var s = new Scenario(); var u = U.Windows(); s.Agent.Inner.Available.Add(u); s.Select(u);
            using (var c = new CancellationTokenSource())
            {
                s.Agent.AfterSearch = n => { if (n == 4) c.Cancel(); };
                Assert.Equal(RunResult.Cancelled, s.Run(c.Token).Result);
                Assert.Empty(s.Agent.Downloaded); Assert.Equal(4, s.Agent.Criteria.Count);
            }
        }
        [Fact]
        public void Legacy_runner_does_not_gain_selected_workflow_exception_handling()
        {
            var s = new Scenario(); var u = U.Windows(); s.Agent.Inner.Available.Add(u); s.Select(u);
            s.Request.SelectionWorkflow = false;
            using (var c = new CancellationTokenSource())
            {
                s.Agent.AfterSearch = n => { c.Cancel(); throw new OperationCanceledException(c.Token); };
                Assert.Equal(RunResult.Failed, s.Run(c.Token).Result);
                Assert.Empty(s.Agent.Downloaded);
            }
        }
        [Fact]
        public void Failed_shared_broad_is_unknown_and_not_retried()
        {
            var s = new Scenario(); var u = U.Windows(); s.Agent.Inner.Available.Add(u); s.Select(u);
            s.Agent.Inner.OnDownload = () => s.Agent.Inner.FailingCriteria.Add(ScanService.BroadCriteria);
            var result = s.Run();
            Assert.True(result.VerificationUnknown); Assert.False(result.PostScan.Complete); Assert.Equal(7, s.Agent.Criteria.Count);
        }
        [Fact]
        public void Newly_appearing_items_are_not_installed_but_appear_in_post_snapshot()
        {
            var s = new Scenario(); var u = U.Windows(); s.Agent.Inner.Available.Add(u); s.Select(u);
            s.Agent.Inner.Available.Add(U.Windows("new"));
            var result = s.Run(); Assert.Single(s.Agent.Downloaded);
            Assert.Equal("new", result.PostScan.IncludedUpdates.Single().Title);
        }
        [Fact]
        public void Readonly_probe_releases_before_search_so_scheduled_run_can_proceed()
        {
            var held = false;
            var h = new Harness();
            ReportOnlyScan.TryRun(() => { held = true; return new CallbackLease(() => held = false); }, () =>
            {
                h.LockBusy = held;
                Assert.Equal(RunResult.NoUpdates, h.Run(RunType.Scheduled).Result);
                return new ScanService(h.Agent, h.Log).Scan(UpdateScope.Default());
            });
            Assert.False(held);
            var called = false;
            Assert.Null(ReportOnlyScan.TryRun(() => null, () => { called = true; return null; }));
            Assert.False(called);
        }
        private sealed class CallbackLease : IDisposable
        {
            private readonly Action release;
            public CallbackLease(Action release) { this.release = release; }
            public void Dispose() => release();
        }
        [Fact]
        public void Reportonly_discovery_uses_three_queries_and_no_registration_or_download()
        {
            var s = new Scenario(); new ScanService(s.Agent, s.Log).Scan(s.Scope);
            Assert.Equal(3, s.Agent.Criteria.Count); Assert.Empty(s.Agent.Downloaded);
            Assert.Equal(0, s.Agent.Registrations); Assert.Equal(0, s.Probe.ServicesStarted); Assert.Equal(0, s.Probe.ServicesStopped);
        }
        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, true, false)]
        [InlineData(true, false, false)]
        [InlineData(true, true, false)]
        [InlineData(false, false, true)]
        public void Equivalent_scenarios_never_exceed_rc3_query_budget(bool fallback, bool reboot, bool missing)
        {
            Func<bool, int> count = optimized =>
            {
                var s = new Scenario(); var u = U.Windows(); s.Agent.Inner.Available.Add(u); s.Select(u);
                s.Request.SelectionWorkflow = optimized; s.Agent.Inner.RebootAfterInstall = reboot;
                if (missing) s.Agent.Inner.Available.Clear();
                if (fallback) s.Agent.Context = SearchContext.Unknown;
                var outcome = s.Run();
                if (!optimized && (outcome.Result == RunResult.Succeeded || outcome.Result == RunResult.SucceededWithErrors) && !outcome.RebootRequired)
                    new ScanService(s.Agent, s.Log).Scan(s.Scope); // RC3 GUI refresh.
                return s.Agent.Criteria.Count;
            };
            Assert.True(count(true) <= count(false));
        }
        [Fact]
        public void Restart_detected_at_shared_broad_ends_with_one_verification_query()
        {
            var s = new Scenario(); var u = U.Windows(); s.Agent.Inner.Available.Add(u); s.Select(u);
            s.Agent.AfterSearch = n => { if (n == 5) s.Probe.PendingAfterFirstCheck = () => FakeProbe.PendingNow(); };
            var result = s.Run();
            Assert.Equal(RunResult.RebootRequired, result.Result); Assert.Null(result.PostScan); Assert.Equal(5, s.Agent.Criteria.Count);
        }
        [Fact]
        public void Every_query_changes_context_but_fallback_stays_at_three_and_proceeds()
        {
            var s = new Scenario(); var u = U.Windows(); s.Agent.Inner.Available.Add(u); s.Select(u);
            s.Agent.AfterSearch = n => s.Agent.Context = Known(n % 2 != 0);
            Assert.Equal(RunResult.Succeeded, s.Run().Result); Assert.Equal(9, s.Agent.Criteria.Count);
        }
        [Fact]
        public void Late_restart_at_gui_refresh_gate_does_not_expand_broad()
        {
            var s = new Scenario(); var u = U.Windows(); s.Agent.Inner.Available.Add(u); s.Select(u);
            var postChecks = 0;
            s.Probe.PendingAfterFirstCheck = () => ++postChecks >= 2 ? FakeProbe.PendingNow() : WinTool.WindowsUpdate.Logic.PendingRestartState.None;
            var result = s.Run();
            Assert.Equal(RunResult.RebootRequired, result.Result); Assert.Null(result.PostScan); Assert.Equal(5, s.Agent.Criteria.Count);
        }
        [Fact]
        public void Scope_change_uses_full_validation_and_new_firmware_flag_is_rechecked()
        {
            var s = new Scenario(); var u = U.Windows(); s.Agent.Inner.Available.Add(u); s.Select(u);
            s.Request.Scope = new UpdateScope(true, true, false, false);
            s.Agent.Inner.Available.Clear(); s.Agent.Inner.Available.Add(new UpdateItem(u.UpdateId, u.Revision, "Vendor BIOS update"));
            var result = s.Run();
            Assert.Equal(6, s.Agent.Criteria.Count); Assert.Empty(s.Agent.Downloaded);
            Assert.Equal(SelectionSkipReason.Policy, result.SelectionSkipped.Single().Reason);
        }
        [Fact]
        public void Required_driver_failure_aborts_even_after_successful_broad()
        {
            var s = new Scenario(); s.Agent.Split = true; var w = U.Windows(); var d = U.Driver(); s.Agent.Inner.Available.AddRange(new[] { w, d }); s.Select(w, d);
            s.Agent.Inner.FailingCriteria.Add(ScanService.DriverCriteria);
            Assert.Equal(RunResult.Failed, s.Run().Result); Assert.Empty(s.Agent.Downloaded); Assert.Equal(5, s.Agent.Criteria.Count);
        }
        [Fact]
        public void Collision_at_busy_probe_skips_scheduled_run_without_retry()
        {
            var h = new Harness { LockBusy = true };
            var outcome = h.Run(RunType.Scheduled);
            Assert.Equal(RunResult.AlreadyRunning, outcome.Result); Assert.Equal(21, ExitCodes.For(outcome.Result));
            Assert.Empty(h.Retries.Scheduled); Assert.Equal(0, h.Agent.Searches); Assert.Equal(0, h.Store.StateSaves);
        }
        private sealed class FakeClock : QueryClock
        {
            private long tick;
            public override long Timestamp => tick += 25;
            public override long Frequency => 1000;
            public override DateTime UtcNow => new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(tick);
        }
        [Fact]
        public void Query_timing_has_deterministic_wall_and_monotonic_measurements()
        {
            var s = new Scenario(); new ScanService(s.Agent, s.Log, new FakeClock()).Query("Broad");
            Assert.True(s.Log.Contains("elapsedMs=25.000")); Assert.True(s.Log.Contains("tick=25")); Assert.True(s.Log.Contains("tick=50"));
        }
    }
}
