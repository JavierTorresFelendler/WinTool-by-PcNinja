using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WinTool.WindowsUpdate.Engine;
using WinTool.WindowsUpdate.Logic;
using WinTool.WindowsUpdate.Model;
using WinTool.WindowsUpdate.Schedule;
using WinTool.WindowsUpdate.Storage;

namespace WinTool.WindowsUpdate.Tests
{
    internal static class U
    {
        private static int next;

        public static UpdateItem Windows(string title = null) => Make(title ?? "Cumulative Update for Windows", false, false);
        public static UpdateItem Optional(string title = null) => Make(title ?? "Optional preview update", false, true);
        public static UpdateItem Driver(string title = null, string driverClass = null) { var u = Make(title ?? "Intel - Display - driver", true, false); u.DriverClass = driverClass; return u; }

        private static UpdateItem Make(string title, bool driver, bool browseOnly)
        {
            var id = Interlocked.Increment(ref next);
            return new UpdateItem("00000000-0000-0000-0000-" + id.ToString("D12"), 1, title) { IsDriverType = driver, BrowseOnly = browseOnly };
        }
    }

    /// <summary>A Windows Update Agent in memory. Installed updates disappear from later searches.</summary>
    internal sealed class FakeAgent : IUpdateAgent
    {
        public List<UpdateItem> Available { get; } = new List<UpdateItem>();
        public Dictionary<string, int> DownloadFailures { get; } = new Dictionary<string, int>();
        public HashSet<string> InstallFailures { get; } = new HashSet<string>();
        public Queue<List<UpdateItem>> AppearAfterInstall { get; } = new Queue<List<UpdateItem>>();
        public bool RebootAfterInstall { get; set; }
        public int? SearchErrorHResult { get; set; }
        public HashSet<string> FailingCriteria { get; } = new HashSet<string>();
        public OperationResult? ForcedInstallResult { get; set; }
        public int Searches { get; private set; }
        public List<string> Installed { get; } = new List<string>();
        public List<string> EulasAccepted { get; } = new List<string>();
        public Action OnDownload { get; set; }

        public bool EnsureMicrosoftUpdateRegistered() => true;

        public IReadOnlyList<UpdateItem> Search(string criteria)
        {
            Searches++;
            if (SearchErrorHResult.HasValue || FailingCriteria.Contains(criteria))
            {
                throw new UpdateAgentException("search failed", SearchErrorHResult ?? unchecked((int)0x80240FFF));
            }

            IEnumerable<UpdateItem> found = Available;
            if (criteria == ScanService.DriverCriteria) found = found.Where(u => u.IsDriverType);
            if (criteria == ScanService.OptionalCriteria) found = found.Where(u => u.BrowseOnly);

            // A fresh object per search, like COM, so merging is really tested.
            return found.Select(u => new UpdateItem(u.UpdateId, u.Revision, u.Title)
            {
                IsDriverType = u.IsDriverType,
                BrowseOnly = u.BrowseOnly,
                DriverClass = u.DriverClass,
                EulaAccepted = u.EulaAccepted
            }).ToList();
        }

        public void AcceptEula(UpdateItem update)
        {
            EulasAccepted.Add(update.Key);
            update.EulaAccepted = true;
        }

        public ItemResult Download(UpdateItem update, CancellationToken cancellation)
        {
            OnDownload?.Invoke();
            int hresult;
            if (DownloadFailures.TryGetValue(update.Title, out hresult)) return new ItemResult(update, OperationResult.Failed, hresult);
            update.IsDownloaded = true;
            return new ItemResult(update, OperationResult.Succeeded, 0);
        }

        public InstallOutcome Install(IReadOnlyList<UpdateItem> updates)
        {
            var results = new List<ItemResult>();
            foreach (var update in updates)
            {
                if (InstallFailures.Contains(update.Title))
                {
                    results.Add(new ItemResult(update, OperationResult.Failed, unchecked((int)0x80070643)));
                    continue;
                }

                Installed.Add(update.Title);
                Available.RemoveAll(a => a.Key == update.Key);
                results.Add(new ItemResult(update, OperationResult.Succeeded, 0));
            }

            if (AppearAfterInstall.Count > 0) Available.AddRange(AppearAfterInstall.Dequeue());
            var overall = ForcedInstallResult ?? (results.All(r => !r.Succeeded) ? OperationResult.Failed : results.Any(r => !r.Succeeded) ? OperationResult.SucceededWithErrors : OperationResult.Succeeded);
            return new InstallOutcome(overall, RebootAfterInstall, results);
        }
    }

    internal sealed class FakeProbe : ISystemProbe
    {
        public PendingRestartState Pending { get; set; } = PendingRestartState.None;
        public Func<PendingRestartState> PendingAfterFirstCheck { get; set; }
        public WindowsUpdateActivity Activity { get; set; } = WindowsUpdateActivity.Idle;
        public bool NetworkReady { get; set; } = true;
        public int ServicesStopped { get; private set; }
        public int ServicesStarted { get; private set; }
        private int pendingChecks;

        public PendingRestartState GetPendingRestart()
        {
            pendingChecks++;
            return pendingChecks > 1 && PendingAfterFirstCheck != null ? PendingAfterFirstCheck() : Pending;
        }

        public WindowsUpdateActivity GetActivity() => Activity;
        public NetworkReadiness CheckNetwork() => new NetworkReadiness(NetworkReady, NetworkReady ? "OK" : "No such host", NetworkReady ? "OK" : "Unable to connect");
        public void EnsureUpdateServicesRunning() => ServicesStarted++;
        public void StopUpdateServices() => ServicesStopped++;

        public static PendingRestartState PendingNow() =>
            new PendingRestartState(new[] { PendingRestartEvaluator.CbsReason }, new string[0], new FileRenameOperation[0]);
    }

    internal sealed class MemoryStore : IWindowsUpdateStore
    {
        public WindowsUpdateSettings Settings { get; set; } = new WindowsUpdateSettings();
        public WindowsUpdateState State { get; set; } = new WindowsUpdateState();
        public int StateSaves { get; private set; }
        public WindowsUpdateSettings LoadSettings() => Settings.Clone();
        public void SaveSettings(WindowsUpdateSettings settings) => Settings = settings;
        public WindowsUpdateState LoadState() => State;
        public void SaveState(WindowsUpdateState state) { State = state; StateSaves++; }
    }

    internal sealed class FakeRetries : IRetryScheduler
    {
        public List<Tuple<DateTime, string, int>> Scheduled { get; } = new List<Tuple<DateTime, string, int>>();
        public int Cancels { get; private set; }
        public void ScheduleRetry(DateTime runAt, string reason, int retryNumber) => Scheduled.Add(Tuple.Create(runAt, reason, retryNumber));
        public void CancelRetry() => Cancels++;
    }

    internal sealed class ListLog : IRunLog
    {
        public List<Tuple<int, EventLevel, string>> Entries { get; } = new List<Tuple<int, EventLevel, string>>();
        public void Write(int eventId, EventLevel level, string message) => Entries.Add(Tuple.Create(eventId, level, message));
        public bool Contains(string text) => Entries.Any(e => e.Item3.Contains(text));
    }

    internal sealed class FakeTasks : ITaskSchedulerService
    {
        public List<TaskEntry> All { get; } = new List<TaskEntry>();
        public HashSet<string> Undeletable { get; } = new HashSet<string>();
        public List<string> Started { get; } = new List<string>();

        public void Register(string name, string xml)
        {
            All.RemoveAll(t => t.Folder == TaskNames.Folder && t.Name == name);
            All.Add(new TaskEntry { Folder = TaskNames.Folder, Name = name, Xml = xml, State = "Ready" });
        }

        public bool Delete(string folder, string name)
        {
            if (Undeletable.Contains(folder + "|" + name)) throw new UnauthorizedAccessException("Access is denied.");
            return All.RemoveAll(t => t.Folder == folder && t.Name == name) > 0;
        }

        public TaskEntry Get(string name) => All.FirstOrDefault(t => t.Folder == TaskNames.Folder && t.Name == name);
        public IReadOnlyList<TaskEntry> Find(Func<string, bool> nameMatches) => All.Where(t => nameMatches(t.Name)).ToList();
        public void Run(string name) => Started.Add(name);
    }

    internal sealed class Harness
    {
        public FakeAgent Agent { get; } = new FakeAgent();
        public FakeProbe Probe { get; } = new FakeProbe();
        public MemoryStore Store { get; } = new MemoryStore();
        public FakeRetries Retries { get; } = new FakeRetries();
        public ListLog Log { get; } = new ListLog();
        public DateTime Now { get; set; } = new DateTime(2026, 11, 6, 10, 0, 0);
        public bool LockBusy { get; set; }

        public UpdateRunner Runner => new UpdateRunner(Agent, Probe, Store, Retries, Log, () => Now, () => LockBusy ? null : new Dummy());

        public RunOutcome Run(RunType type = RunType.Manual, bool interactive = false, Action<RunRequest> configure = null, CancellationToken cancellation = default(CancellationToken))
        {
            var request = new RunRequest { Type = type };
            configure?.Invoke(request);
            return Runner.Run(request, null, cancellation, interactive);
        }

        private sealed class Dummy : IDisposable
        {
            public void Dispose() { }
        }
    }
}
