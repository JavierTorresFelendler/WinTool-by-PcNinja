using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WinTool.WindowsUpdate.Logic;
using WinTool.WindowsUpdate.Model;
using WinTool.WindowsUpdate.Storage;

namespace WinTool.WindowsUpdate.Engine
{
    /// <summary>
    /// One managed Windows Update run: checks, passes, retry and state
    /// (WU-20 to WU-63). The same code serves the window, the command line
    /// and the scheduled task.
    /// </summary>
    public sealed class UpdateRunner
    {
        private static readonly RunResult[] ClearsRetry =
        {
            RunResult.Succeeded, RunResult.SucceededWithErrors, RunResult.NoUpdates, RunResult.RebootRequired, RunResult.MaxPassesReached
        };

        private readonly IUpdateAgent agent;
        private readonly ISystemProbe probe;
        private readonly IWindowsUpdateStore store;
        private readonly IRetryScheduler retries;
        private readonly IRunLog log;
        private readonly Func<DateTime> clock;
        private readonly Func<IDisposable> acquireOperationLock;

        public UpdateRunner(IUpdateAgent agent, ISystemProbe probe, IWindowsUpdateStore store, IRetryScheduler retries, IRunLog log,
            Func<DateTime> clock, Func<IDisposable> acquireOperationLock)
        {
            this.agent = agent;
            this.probe = probe;
            this.store = store;
            this.retries = retries;
            this.log = log;
            this.clock = clock ?? (() => DateTime.Now);
            this.acquireOperationLock = acquireOperationLock;
        }

        public RunOutcome Run(RunRequest request, IProgress<RunProgress> progress = null, CancellationToken cancellation = default(CancellationToken), bool interactive = false)
        {
            var heldLock = acquireOperationLock();
            if (heldLock == null)
            {
                log.Write(EventIds.RunSkipped, EventLevel.Warning, "Another WinTool operation is running. This run does not start.");
                return new RunOutcome(RunResult.AlreadyRunning, "Another WinTool operation is already running.");
            }

            using (heldLock)
            {
                var settings = store.LoadSettings();
                var state = store.LoadState();
                try
                {
                    return RunLocked(request, settings, state, progress, cancellation, interactive);
                }
                catch (Exception ex)
                {
                    var hresult = (ex as UpdateAgentException)?.HResult ?? ex.HResult;
                    var message = ex is UpdateAgentException agentError ? agentError.Describe() : HResults.Describe(ex.Message, hresult);
                    log.Write(EventIds.RunFailed, EventLevel.Error, "Windows Update run failed: " + message);

                    if (!interactive && HResults.IsRetryable(hresult))
                    {
                        var retry = RequestRetry(settings, state, message);
                        return Finish(request.Type, state, retry, false);
                    }

                    return Finish(request.Type, state, new RunOutcome(RunResult.Failed, message), false);
                }
            }
        }

        private RunOutcome RunLocked(RunRequest request, WindowsUpdateSettings settings, WindowsUpdateState state, IProgress<RunProgress> progress, CancellationToken cancellation, bool interactive)
        {
            var now = clock();

            // WU-55: cooldown for automatic runs only.
            if (request.Type != RunType.Manual && request.Type != RunType.Retry && settings.MinimumCooldownMinutes > 0)
            {
                var lastFinished = WindowsUpdateState.Parse(state.LastRunFinished);
                if (lastFinished.HasValue && (now - lastFinished.Value).TotalMinutes < settings.MinimumCooldownMinutes)
                {
                    var minutes = (int)(now - lastFinished.Value).TotalMinutes;
                    var message = "Cooldown active. Last run finished " + minutes + " minute(s) ago; minimum cooldown is " + settings.MinimumCooldownMinutes + " minute(s).";
                    log.Write(EventIds.CooldownActive, EventLevel.Warning, message);
                    return Finish(request.Type, state, new RunOutcome(RunResult.CooldownActive, message), false);
                }
            }

            state.LastRunStarted = WindowsUpdateState.Stamp(now);
            state.LastRunType = request.Type.ToString();
            state.LastResult = "Running";
            state.LastMessage = string.Empty;
            if (request.Type != RunType.Retry) ClearRetry(state);
            store.SaveState(state);
            log.Write(EventIds.RunStarted, EventLevel.Information, request.Type + " Windows Update run started.");
            Report(progress, RunStage.Checking, 0, 0, 0, null);

            // WU-21/WU-40: a pending restart stops the run before anything else.
            var pending = probe.GetPendingRestart();
            if (pending.Pending)
            {
                var message = "A restart is already pending. Reasons: " + string.Join("; ", pending.Reasons);
                log.Write(EventIds.RestartPending, EventLevel.Warning, message);
                return Finish(request.Type, state, new RunOutcome(RunResult.RebootRequired, message) { RebootRequired = true }, true);
            }

            // WU-32: scheduled and command-line runs retry later; a run from the window reports it.
            var network = probe.CheckNetwork();
            if (!network.Ready)
            {
                var message = "The network is not ready. " + network.Message;
                log.Write(EventIds.NetworkNotReady, EventLevel.Warning, message);
                var outcome = interactive ? new RunOutcome(RunResult.Failed, message) : RequestRetry(settings, state, message);
                return Finish(request.Type, state, outcome, false);
            }

            // WU-30/WU-31: never interrupt Windows; a manual run may snooze it.
            var activity = probe.GetActivity();
            if (activity.Status != ActivityStatus.Idle)
            {
                log.Write(EventIds.WindowsBusy, EventLevel.Warning, activity.Message);
                var decision = request.Type == RunType.Manual && request.OnWindowsBusy != null
                    ? request.OnWindowsBusy(activity)
                    : BusyDecision.RetryLater;

                if (decision == BusyDecision.Cancel)
                {
                    return Finish(request.Type, state, new RunOutcome(RunResult.Cancelled, "Cancelled because Windows Update is busy."), false);
                }

                if (decision == BusyDecision.RetryLater)
                {
                    var outcome = interactive
                        ? new RunOutcome(activity.IsInstalling ? RunResult.SkippedInstalling : RunResult.SkippedBackgroundActivity, activity.Message)
                        : RequestRetry(settings, state, activity.Message);
                    return Finish(request.Type, state, outcome, false);
                }

                log.Write(EventIds.WindowsUpdateSnoozed, EventLevel.Warning, "Snooze selected: stopping the Windows Update services, then starting them again for this run.");
                probe.StopUpdateServices();
            }

            probe.EnsureUpdateServicesRunning();
            agent.EnsureMicrosoftUpdateRegistered();

            var scope = request.Scope ?? UpdateScope.Default(settings.InstallFirmwareUpdates);
            var result = RunPasses(request, scope, progress, cancellation);
            if (result.Result == RunResult.DownloadFailed && !interactive && result.Items.Any(i => HResults.IsRetryable(i.HResult)))
            {
                // WU-63: a download that failed on a network error is retried like WU-60.
                var reason = "Download failed: " + HResults.Describe("network error", result.Items.First(i => HResults.IsRetryable(i.HResult)).HResult);
                var retry = RequestRetry(settings, state, reason);
                retry.Items.AddRange(result.Items);
                retry.UpdatesFound = result.UpdatesFound;
                return Finish(request.Type, state, retry, false);
            }

            return Finish(request.Type, state, result, ClearsRetry.Contains(result.Result));
        }

        private RunOutcome RunPasses(RunRequest request, UpdateScope scope, IProgress<RunProgress> progress, CancellationToken cancellation)
        {
            var scanner = new ScanService(agent, log);
            var maxPasses = request.SelectedKeys != null ? 1 : Math.Max(1, Math.Min(10, request.MaxPasses));
            var totalFound = 0;
            var passesCompleted = 0;
            var lastInstall = RunResult.NoUpdates;
            var items = new List<ItemResult>();

            // An update that failed once is not tried again in the same run; it would fail the same way.
            var failedKeys = new HashSet<string>();

            log.Write(EventIds.RunStarted, EventLevel.Information, "Update scope: " + scope + (scope.Firmware ? ". Firmware/BIOS updates are enabled for this run." : ". Firmware/BIOS updates are detected but skipped."));

            for (var pass = 1; pass <= maxPasses; pass++)
            {
                if (pass > 1)
                {
                    var pendingBefore = probe.GetPendingRestart();
                    if (pendingBefore.Pending)
                    {
                        return Outcome(RunResult.RebootRequired, "Restart required before the next update pass.", totalFound, passesCompleted, items, true);
                    }
                }

                if (cancellation.IsCancellationRequested) return Outcome(RunResult.Cancelled, "Cancelled.", totalFound, passesCompleted, items, false);

                Report(progress, RunStage.Searching, pass, 0, 0, null);
                var scan = scanner.Scan(scope, request.SelectedKeys, "Pass " + pass);
                if (scan.Updates.Count == 0)
                {
                    var none = passesCompleted == 0
                        ? "No applicable Windows, optional or driver updates were found."
                        : "No remaining updates after " + passesCompleted + " completed pass(es).";
                    return Outcome(passesCompleted == 0 ? RunResult.NoUpdates : WithFailures(lastInstall, failedKeys), none, totalFound, passesCompleted, items, false);
                }

                foreach (var skipped in scan.Updates.Where(u => !u.Included))
                {
                    log.Write(skipped.Excluded == ExclusionReason.Firmware ? EventIds.UpdateSkippedFirmware : EventIds.UpdateSkippedScope, EventLevel.Information,
                        "Pass " + pass + " skips (" + skipped.Excluded + "): " + skipped.Update.Title);
                }

                var queue = scan.IncludedUpdates.Where(u => !failedKeys.Contains(u.Key)).ToList();
                totalFound += queue.Count;
                if (queue.Count == 0 && failedKeys.Count > 0)
                {
                    return Outcome(WithFailures(lastInstall, failedKeys), "The remaining updates failed earlier in this run.", totalFound, passesCompleted, items, false);
                }

                if (queue.Count == 0)
                {
                    string message;
                    if (scan.FirmwareSkipped > 0) message = "Only firmware/BIOS updates were found (" + scan.FirmwareSkipped + "), and firmware installation is off.";
                    else if (scan.ScopeSkipped > 0) message = "Only updates outside the selected scope were found (" + scan.ScopeSkipped + ").";
                    else message = "No installable updates remained after filtering.";
                    return Outcome(passesCompleted == 0 ? RunResult.NoUpdates : lastInstall, message, totalFound, passesCompleted, items, false);
                }

                foreach (var update in queue.Where(u => !u.EulaAccepted)) agent.AcceptEula(update);

                // Download one by one: progress, per-update results and cancel (WU-23, WU-24).
                var downloaded = new List<UpdateItem>();
                for (var i = 0; i < queue.Count; i++)
                {
                    if (cancellation.IsCancellationRequested) return Outcome(RunResult.Cancelled, "Cancelled during download. Nothing from this pass was installed.", totalFound, passesCompleted, items, false);
                    var update = queue[i];
                    Report(progress, RunStage.Downloading, pass, i + 1, queue.Count, update.Title);
                    var result = agent.Download(update, cancellation);
                    if (result.Succeeded || update.IsDownloaded)
                    {
                        downloaded.Add(update);
                    }
                    else
                    {
                        failedKeys.Add(update.Key);
                        items.Add(result);
                        log.Write(EventIds.UpdateItemFailed, EventLevel.Warning, "Download failed: " + update.Title + " " + HResults.Describe(result.Result.ToString(), result.HResult));
                    }
                }

                log.Write(EventIds.DownloadResult, EventLevel.Information, "Pass " + pass + " downloaded " + downloaded.Count + " of " + queue.Count + " update(s).");
                if (downloaded.Count == 0)
                {
                    return Outcome(RunResult.DownloadFailed, "No update could be downloaded.", totalFound, passesCompleted, items, false);
                }

                Report(progress, RunStage.Installing, pass, 0, downloaded.Count, null);
                var install = agent.Install(downloaded);
                passesCompleted = pass;
                items.AddRange(install.Items);
                foreach (var failed in install.Items.Where(i => !i.Succeeded))
                {
                    failedKeys.Add(failed.Update.Key);
                    log.Write(EventIds.UpdateItemFailed, EventLevel.Error, "Install failed: " + failed.Update.Title + " " + HResults.Describe(failed.Result.ToString(), failed.HResult));
                }

                var anyItemFailed = install.Items.Any(i => !i.Succeeded) || downloaded.Count < queue.Count;
                switch (install.Result)
                {
                    case OperationResult.Succeeded:
                        lastInstall = anyItemFailed ? RunResult.SucceededWithErrors : RunResult.Succeeded;
                        break;
                    case OperationResult.SucceededWithErrors:
                        lastInstall = RunResult.SucceededWithErrors;
                        break;
                    default:
                        lastInstall = RunResult.Failed;
                        break;
                }

                log.Write(EventIds.InstallResult, lastInstall == RunResult.Failed ? EventLevel.Error : EventLevel.Information,
                    "Pass " + pass + " installed " + install.Items.Count(i => i.Succeeded) + " of " + downloaded.Count + " update(s). Result: " + install.Result + ".");

                // WU-26: verification search, logged only.
                Report(progress, RunStage.Verifying, pass, 0, 0, null);
                try
                {
                    var remaining = agent.Search(ScanService.BroadCriteria);
                    log.Write(EventIds.SearchResult, EventLevel.Information, "After pass " + pass + ", " + remaining.Count + " update(s) remain.");
                }
                catch (UpdateAgentException ex)
                {
                    log.Write(EventIds.SearchFailed, EventLevel.Warning, "Verification search after pass " + pass + " failed. " + ex.Describe());
                }

                var pendingAfter = probe.GetPendingRestart();
                if (install.RebootRequired || pendingAfter.Pending)
                {
                    return Outcome(RunResult.RebootRequired, "Updates were installed. Restart the computer to finish; more updates may appear after the restart.", totalFound, passesCompleted, items, true);
                }

                if (lastInstall == RunResult.Failed)
                {
                    return Outcome(RunResult.Failed, "Installation failed. Result: " + install.Result + ".", totalFound, passesCompleted, items, false);
                }
            }

            if (request.SelectedKeys != null)
            {
                // A run of selected updates is one pass by design (WU-27); its result is that pass.
                return Outcome(WithFailures(lastInstall, failedKeys), "The selected updates were processed.", totalFound, passesCompleted, items, false);
            }

            return Outcome(RunResult.MaxPassesReached, "Maximum update passes reached (" + maxPasses + "). Run again, or restart if Windows asks for it.", totalFound, passesCompleted, items, false);
        }

        private static RunResult WithFailures(RunResult result, ICollection<string> failedKeys)
        {
            return result == RunResult.Succeeded && failedKeys.Count > 0 ? RunResult.SucceededWithErrors : result;
        }

        /// <summary>WU-60 to WU-62.</summary>
        private RunOutcome RequestRetry(WindowsUpdateSettings settings, WindowsUpdateState state, string reason)
        {
            if (!settings.AutoRetryEnabled)
            {
                log.Write(EventIds.RetryNotScheduled, EventLevel.Warning, "Automatic retry is off. Reason: " + reason);
                return new RunOutcome(RunResult.RetryDisabled, reason);
            }

            var next = state.RetryCount + 1;
            if (next > Math.Max(0, settings.RetryMaxAttempts))
            {
                log.Write(EventIds.RetryNotScheduled, EventLevel.Warning, "Retry limit reached (" + settings.RetryMaxAttempts + "). Reason: " + reason);
                return new RunOutcome(RunResult.MaxRetriesReached, reason);
            }

            var delay = RetryPolicy.DelayMinutes(settings, next);
            var runAt = clock().AddMinutes(delay);
            retries.ScheduleRetry(runAt, reason, next);
            state.RetryCount = next;
            state.LastRetryScheduled = WindowsUpdateState.Stamp(runAt);
            state.LastRetryReason = reason;
            log.Write(EventIds.RetryScheduled, EventLevel.Warning, "Retry " + next + " scheduled for " + runAt.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " (in " + delay + " minute(s)). Reason: " + reason);
            return new RunOutcome(RunResult.RetryScheduled, "Retry scheduled for " + runAt.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture) + ". " + reason) { RetryAt = runAt };
        }

        private void ClearRetry(WindowsUpdateState state)
        {
            if (state.RetryCount != 0 || state.LastRetryScheduled != null)
            {
                log.Write(EventIds.RetryCancelled, EventLevel.Information, "Pending retry cleared.");
            }

            state.RetryCount = 0;
            state.LastRetryScheduled = null;
            state.LastRetryReason = null;
            retries.CancelRetry();
        }

        private RunOutcome Finish(RunType type, WindowsUpdateState state, RunOutcome outcome, bool clearRetry)
        {
            var now = clock();
            state.LastRunFinished = WindowsUpdateState.Stamp(now);
            state.LastRunType = type.ToString();
            state.LastResult = outcome.Result.ToString();
            state.LastMessage = outcome.Message;
            state.LastRebootRequired = outcome.RebootRequired;
            if (outcome.Result == RunResult.Succeeded || outcome.Result == RunResult.SucceededWithErrors || outcome.Result == RunResult.NoUpdates)
            {
                state.LastSuccessfulRun = WindowsUpdateState.Stamp(now);
            }

            if (clearRetry) ClearRetry(state);
            store.SaveState(state);
            log.Write(EventIds.RunFinished, outcome.Result == RunResult.Failed ? EventLevel.Error : EventLevel.Information,
                type + " run finished: " + outcome.Result + ". " + outcome.Message);
            return outcome;
        }

        private static RunOutcome Outcome(RunResult result, string message, int found, int passes, List<ItemResult> items, bool reboot)
        {
            var outcome = new RunOutcome(result, message) { UpdatesFound = found, PassesCompleted = passes, RebootRequired = reboot };
            outcome.Items.AddRange(items);
            return outcome;
        }

        private static void Report(IProgress<RunProgress> progress, RunStage stage, int pass, int current, int total, string title)
        {
            progress?.Report(new RunProgress(stage, pass, current, total, title));
        }
    }
}
