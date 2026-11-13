using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using WinTool.Cli;
using WinTool.Core.Diagnostics;
using WinTool.WindowsUpdate.Engine;
using WinTool.WindowsUpdate.Logic;
using WinTool.WindowsUpdate.Model;
using WinTool.WindowsUpdate.Schedule;
using WinTool.WindowsUpdate.Storage;

namespace WinTool.App.ViewModels
{
    public sealed class UpdateRowViewModel : LocalizedObject
    {
        private bool isSelected;

        public UpdateRowViewModel(ScannedUpdate scanned)
        {
            Scanned = scanned;
            isSelected = scanned.Included;
        }

        public ScannedUpdate Scanned { get; }
        public string Key => Scanned.Update.Key;
        public string Title => Scanned.Update.Title;

        public string Category
        {
            get
            {
                if (Scanned.IsFirmware) return T["WU_Kind_Firmware"];
                switch (Scanned.Kind)
                {
                    case UpdateKind.Driver: return T["WU_Kind_Driver"];
                    case UpdateKind.Optional: return T["WU_Kind_Optional"];
                    default: return T["WU_Kind_Important"];
                }
            }
        }

        public bool IsImportant => !Scanned.IsFirmware && Scanned.Kind == UpdateKind.Windows;

        public string Size => Scanned.Update.MaxDownloadSize > 0
            ? Math.Max(1, Scanned.Update.MaxDownloadSize / 1048576).ToString(CultureInfo.InvariantCulture) + " MB"
            : string.Empty;

        public bool IsSelected
        {
            get => isSelected;
            set => Set(ref isSelected, value);
        }

        private string result;

        public string Result
        {
            get => result;
            set => Set(ref result, value);
        }
    }

    public sealed class HealthRowViewModel : LocalizedObject
    {
        private readonly Func<string> text;

        public HealthRowViewModel(Func<string> text, HealthStatus status)
        {
            this.text = text;
            Status = status;
        }

        public string Text => text();
        public HealthStatus Status { get; }
        public string StatusName => Status.ToString();
        public bool IsOk => Status == HealthStatus.Ok;
        public bool IsWarning => Status == HealthStatus.Warning;
        public bool IsProblem => Status == HealthStatus.Problem;
    }

    public sealed class DayOption
    {
        public DayOption(string value, string display)
        {
            Value = value;
            Display = display;
        }

        public string Value { get; }
        public string Display { get; }
    }

    /// <summary>
    /// The Windows Update screen: everything on one page (owner decision):
    /// status, available updates, health and maintenance, and the schedule.
    /// </summary>
    public sealed class WindowsUpdatePageViewModel : PageViewModel, IOperationPage
    {
        private readonly IWindowsUpdateEnvironment env;
        private CancellationTokenSource cancellation;
        private bool loaded;
        private bool busy;
        private RunStage runStage = RunStage.Checking;
        private bool hasScanned;
        private DateTime? lastScan;
        private PendingRestartState pending = PendingRestartState.None;
        private Func<string> message;
        private bool messageIsError;
        private string progressText;
        private double progressValue;
        private bool progressIndeterminate;
        private string healthSummary;
        private HealthStatus healthOverall = HealthStatus.Ok;
        private bool legacyDetected;

        // Schedule fields, edited in place.
        private bool scheduleEnabled;
        private string frequency;
        private string dayOfWeek;
        private int monthlyDay;
        private string time;
        private bool runIfMissed;
        private bool runAtStartup;
        private bool wakeToRun;
        private bool autoRetry;
        private bool rebootPrompt;
        private bool installFirmware;
        private string nextRun;
        private Func<string> scheduleMessage;
        private bool scheduleMessageIsError;

        public WindowsUpdatePageViewModel(IWindowsUpdateEnvironment env)
            : base("Nav_WindowsUpdate", "Page_WindowsUpdate_Subtitle")
        {
            this.env = env;
            ScanCommand = new AsyncCommand(ScanAsync, () => !busy);
            InstallCommand = new AsyncCommand(InstallAsync, () => !busy && !pending.Pending && Rows.Any(r => r.IsSelected));
            CancelCommand = new RelayCommand(CancelCurrentOperation, () => busy && cancellation != null && !cancellation.IsCancellationRequested);
            RestartCommand = new RelayCommand(RestartNow, () => pending.Pending && !busy);
            HealthCheckCommand = new AsyncCommand(() => HealthAsync(true), () => !busy);
            CollectLogsCommand = new AsyncCommand(CollectLogsAsync, () => !busy);
            ResetCommand = new AsyncCommand(ResetAsync, () => !busy);
            SaveScheduleCommand = new AsyncCommand(SaveScheduleAsync, () => !busy);
            TakeOverCommand = new AsyncCommand(TakeOverAsync, () => !busy && legacyDetected);
        }

        public ObservableCollection<UpdateRowViewModel> Rows { get; } = new ObservableCollection<UpdateRowViewModel>();
        public ObservableCollection<HealthRowViewModel> HealthRows { get; } = new ObservableCollection<HealthRowViewModel>();

        public ICommand ScanCommand { get; }
        public ICommand InstallCommand { get; }
        public ICommand CancelCommand { get; }

        public void RequestCancel()
        {
            if (CancelCommand.CanExecute(null)) CancelCommand.Execute(null);
        }

        private void CancelCurrentOperation()
        {
            var source = cancellation;
            if (source == null || source.IsCancellationRequested) return;
            env.Log.Write(EventIds.CancelRequested, EventLevel.Information,
                "CancelRequested: UI stage=" + runStage + "; requestedAtUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
                ". Waiting for the current Windows operation to finish.");
            source.Cancel();
            OnPropertyChanged(nameof(ProgressText));
            CommandManager.InvalidateRequerySuggested();
        }
        public ICommand RestartCommand { get; }

        /// <summary>Other WinTool operations in this window (Upgrade, Office, an update of WinTool); set by the main view model.</summary>
        public Func<bool> OtherOperationRunning { get; set; } = () => false;
        public ICommand HealthCheckCommand { get; }
        public ICommand CollectLogsCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand SaveScheduleCommand { get; }

        /// <summary>D-014: installs WinTool with these MSI properties; set by the main view model.</summary>
        public Func<string, Task<bool>> InstallForSchedule { get; set; }
        public ICommand TakeOverCommand { get; }

        /// <summary>Raised when the counts or restart state change, for the Dashboard.</summary>
        public event EventHandler SummaryChanged;

        public bool IsBusy { get => busy; private set { if (Set(ref busy, value)) OnPropertyChanged(nameof(IsIdle)); } }
        public bool IsIdle => !busy;
        public bool HasScanned => hasScanned;
        public bool HasUpdates => Rows.Count > 0;
        public bool NoUpdatesFound => hasScanned && Rows.Count == 0;
        public int ImportantCount => Rows.Count(r => r.IsImportant);
        public int OtherCount => Rows.Count(r => !r.IsImportant);

        public string LastCheckText => lastScan.HasValue
            ? T.Format("WU_LastCheck", lastScan.Value.ToString("g", T.Culture))
            : T["WU_NotChecked"];

        public string CountText => hasScanned ? T.Format("WU_Counts", ImportantCount, OtherCount) : T["WU_NotChecked"];

        public bool RestartPending => pending.Pending;
        public bool NoRestartPending => !pending.Pending;
        public string RestartReason => pending.Pending ? string.Join("; ", pending.Reasons) : string.Empty;

        /// <summary>Re-translated on a language change, like every text on the page.</summary>
        public string Message => message?.Invoke() ?? string.Empty;
        public bool HasMessage => !string.IsNullOrEmpty(Message);
        public bool MessageIsError { get => messageIsError; private set => Set(ref messageIsError, value); }

        public string CancelText => runStage == RunStage.Installing ? T["WU_CancelAfterInstall"] : T["WU_Cancel"];
        public string ProgressText { get => cancellation?.IsCancellationRequested == true ? T["WU_CancelPending"] : progressText; private set => Set(ref progressText, value); }
        public double ProgressValue { get => progressValue; private set => Set(ref progressValue, value); }
        public bool ProgressIndeterminate { get => progressIndeterminate; private set => Set(ref progressIndeterminate, value); }

        public string HealthSummary { get => healthSummary; private set => Set(ref healthSummary, value); }
        public HealthStatus HealthOverall { get => healthOverall; private set { if (Set(ref healthOverall, value)) OnPropertyChanged(nameof(HealthOverallName)); } }
        public string HealthOverallName => healthOverall.ToString();

        public bool LegacyDetected { get => legacyDetected; private set => Set(ref legacyDetected, value); }

        public bool ScheduleEnabled { get => scheduleEnabled; set => Set(ref scheduleEnabled, value); }
        public bool IsDaily { get => frequency == Frequencies.Daily; set { if (value) SetFrequency(Frequencies.Daily); } }
        public bool IsWeekly { get => frequency == Frequencies.Weekly; set { if (value) SetFrequency(Frequencies.Weekly); } }
        public bool IsMonthly { get => frequency == Frequencies.Monthly; set { if (value) SetFrequency(Frequencies.Monthly); } }
        public bool IsStartup { get => frequency == Frequencies.Startup; set { if (value) SetFrequency(Frequencies.Startup); } }
        public string DayOfWeek { get => dayOfWeek; set => Set(ref dayOfWeek, value); }
        public int MonthlyDay { get => monthlyDay; set => Set(ref monthlyDay, value); }
        public string Time { get => time; set => Set(ref time, value); }
        public bool RunIfMissed { get => runIfMissed; set => Set(ref runIfMissed, value); }
        public bool RunAtStartup { get => runAtStartup; set => Set(ref runAtStartup, value); }
        public bool WakeToRun { get => wakeToRun; set => Set(ref wakeToRun, value); }
        public bool AutoRetry { get => autoRetry; set => Set(ref autoRetry, value); }
        public bool RebootPrompt { get => rebootPrompt; set => Set(ref rebootPrompt, value); }
        public bool InstallFirmware { get => installFirmware; set => Set(ref installFirmware, value); }
        public string NextRun { get => nextRun; private set => Set(ref nextRun, value); }
        public string ScheduleMessage => scheduleMessage?.Invoke() ?? string.Empty;
        public bool HasScheduleMessage => !string.IsNullOrEmpty(ScheduleMessage);
        public bool ScheduleMessageIsError { get => scheduleMessageIsError; private set => Set(ref scheduleMessageIsError, value); }
        public bool ShowDay => IsWeekly;
        public bool ShowMonthDay => IsMonthly;
        public bool ShowTime => !IsStartup;
        public bool ShowRunAtStartup => !IsStartup;

        public IReadOnlyList<DayOption> Days =>
            Enum.GetValues(typeof(System.DayOfWeek)).Cast<System.DayOfWeek>()
                .Select(d => new DayOption(d.ToString(), T.Culture.DateTimeFormat.GetDayName(d))).ToList();

        public IReadOnlyList<int> MonthDays => Enumerable.Range(1, 28).ToList();

        public override void OnActivated()
        {
            if (loaded) return;
            loaded = true;
            LoadSchedule();
            _ = RefreshStateAsync();
        }

        // ----------------------------------------------------------- operations

        private async Task ScanAsync()
        {
            if (!Begin(T["WU_Progress_Searching"], indeterminate: true)) return;
            try
            {
                var settings = env.Store.LoadSettings();
                var scan = await Task.Run(() => new ScanService(env.CreateAgent(), env.Log).Scan(UpdateScope.Default(settings.InstallFirmwareUpdates)));
                ApplyScan(scan);
                var none = Rows.Count == 0;
                SetMessage(none ? () => T["WU_NoUpdates"] : (Func<string>)null, false);
            }
            catch (UpdateAgentException ex)
            {
                SetMessage(() => T.Format("WU_ScanFailed", ex.Describe()), true);
            }
            finally
            {
                End();
                pending = await Task.Run(() => env.Probe.GetPendingRestart());
                RaiseSummary();
            }
        }

        private async Task InstallAsync()
        {
            var selected = Rows.Where(r => r.IsSelected).ToList();
            if (selected.Any(r => r.Scanned.IsFirmware) && !Dialogs.Confirm(T["WU_Firmware_Warning"])) return;
            if (!Begin(T["WU_Progress_Starting"], indeterminate: true)) return;

            cancellation = new CancellationTokenSource();
            var progressSource = cancellation;
            var progress = new Progress<RunProgress>(value =>
            {
                if (ReferenceEquals(cancellation, progressSource)) OnProgress(value);
            });
            var dispatcher = Application.Current.Dispatcher;
            var request = new RunRequest
            {
                Type = RunType.Manual,
                SelectedKeys = new HashSet<string>(selected.Select(r => r.Key)),
                Scope = UpdateScope.Default(includeFirmware: selected.Any(r => r.Scanned.IsFirmware) || env.Store.LoadSettings().InstallFirmwareUpdates),
                OnWindowsBusy = activity => (BusyDecision)dispatcher.Invoke(new Func<BusyDecision>(() =>
                    Dialogs.Confirm(T.Format("WU_Busy_Question", activity.Message)) ? BusyDecision.Snooze : BusyDecision.Cancel))
            };

            RunOutcome outcome;
            try
            {
                var token = cancellation.Token;
                var runner = new UpdateRunner(env.CreateAgent(), env.Probe, env.Store, new ScheduleService(env.Tasks, env.Log, env.ExecutablePath, env.Clock),
                    env.Log, env.Clock, env.TryAcquireOperationLock);
                outcome = await Task.Run(() => runner.Run(request, progress, token, interactive: true));
            }
            finally
            {
                cancellation.Dispose();
                cancellation = null;
                End();
            }

            foreach (var item in outcome.Items)
            {
                var row = Rows.FirstOrDefault(r => r.Key == item.Update.Key);
                if (row != null) row.Result = item.Succeeded ? T["WU_Item_Installed"] : T.Format("WU_Item_Failed", HResults.ToHex(item.HResult));
            }

            SetMessage(() => Explain(outcome), outcome.Result == RunResult.Failed || outcome.Result == RunResult.DownloadFailed || outcome.Result == RunResult.SucceededWithErrors);
            pending = await Task.Run(() => env.Probe.GetPendingRestart());
            if (outcome.RebootRequired || pending.Pending)
            {
                RaiseSummary();
                // Only claim an installation when this run installed something (owner VM test).
                var installed = outcome.Items.Any(i => i.Succeeded);
                if (env.Store.LoadSettings().ShowRebootPrompt) Dialogs.Info(T[installed ? "WU_RestartPrompt" : "WU_Result_RestartBlocking"]);
                return;
            }

            if (outcome.Result == RunResult.Succeeded || outcome.Result == RunResult.SucceededWithErrors) await ScanAsync();
            RaiseSummary();
        }

        private IDisposable restartLease;

        private void RestartNow()
        {
            // Never during another WinTool operation, here or in a scheduled task (owner VM test).
            IDisposable lease;
            var decision = RestartGuard.Evaluate(env.TryAcquireOperationLock, OtherOperationRunning, env.Probe.GetActivity, out lease);
            if (decision == RestartDecision.OperationRunning)
            {
                Dialogs.Info(T["WU_Restart_Busy"]);
                return;
            }

            var question = decision == RestartDecision.WindowsInstalling ? T["WU_Restart_WindowsBusy"] : T["WU_Restart_Confirm"];
            if (!Dialogs.Confirm(question))
            {
                lease.Dispose();
                return;
            }

            // Held until Windows restarts, so nothing else starts in between.
            restartLease = lease;
            env.Log.Write(EventIds.RunFinished, EventLevel.Information, "Restart requested from the WinTool window.");
            Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false });
        }

        private async Task HealthAsync(bool checkNetwork)
        {
            var settings = env.Store.LoadSettings();
            var items = await Task.Run(() =>
            {
                var facts = env.CollectHealthFacts(checkNetwork);
                if (settings.ScheduleEnabled)
                {
                    facts.ScheduledTaskMatchesSettings = new ScheduleService(env.Tasks, env.Log, env.ExecutablePath, env.Clock).GetStatus(settings).MatchesSettings;
                }

                return HealthCheck.Evaluate(facts);
            });

            HealthRows.Clear();
            foreach (var item in items)
            {
                var row = item;
                HealthRows.Add(new HealthRowViewModel(() => HealthText(row), row.Status));
            }
            HealthOverall = HealthCheck.Overall(items);
            HealthSummary = T["WU_Health_" + HealthOverall];
        }

        private async Task CollectLogsAsync()
        {
            var output = new StringWriter();
            var code = await Task.Run(() => new CommandRunner(env, output, output).Run(new[] { "logs", "collect" }));
            var text = output.ToString();
            var line = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("Log package: ", StringComparison.Ordinal));
            if (code == 0 && line != null)
            {
                var path = line.Substring("Log package: ".Length);
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
                SetMessage(() => T.Format("WU_LogsCollected", path), false);
            }
            else
            {
                SetMessage(() => text.Trim(), true);
            }
        }

        private async Task ResetAsync()
        {
            if (!Dialogs.Confirm(T["WU_Reset_Confirm"])) return;
            if (!Begin(T["WU_Progress_Resetting"], indeterminate: true)) return;
            try
            {
                var result = await Task.Run(() =>
                {
                    using (var held = env.TryAcquireOperationLock())
                    {
                        return held == null ? null : env.CreateReset().Run();
                    }
                });

                if (result == null) SetMessage(() => T["WU_OperationRunning"], true);
                else SetMessage(() => result.Succeeded ? T["WU_Reset_Done"] : T.Format("WU_Reset_Warnings", string.Join(" ", result.Warnings)), !result.Succeeded);
            }
            finally
            {
                End();
            }

            await HealthAsync(false);
        }

        private async Task TakeOverAsync()
        {
            if (!Dialogs.Confirm(T["WU_Legacy_Confirm"])) return;
            var output = new StringWriter();
            var code = await Task.Run(() => new CommandRunner(env, output, output).Run(new[] { "schedule", "import-v2" }));
            if (code == 0)
            {
                TakeoverNotice.Clear(env);
                LegacyDetected = false;
                LoadSchedule();
                Dialogs.Info(T["WU_Legacy_Done"]);
            }
            else
            {
                SetMessage(() => output.ToString().Trim(), true);
            }
        }

        // ------------------------------------------------------------ schedule

        private void LoadSchedule()
        {
            var settings = env.Store.LoadSettings();
            ScheduleEnabled = settings.ScheduleEnabled;
            SetFrequency(settings.Frequency);
            DayOfWeek = settings.DayOfWeek;
            MonthlyDay = settings.MonthlyDay;
            Time = settings.Time;
            RunIfMissed = settings.RunIfMissed;
            RunAtStartup = settings.RunAtStartup;
            WakeToRun = settings.WakeToRun;
            AutoRetry = settings.AutoRetryEnabled;
            RebootPrompt = settings.ShowRebootPrompt;
            InstallFirmware = settings.InstallFirmwareUpdates;
            _ = RefreshNextRunAsync(settings);
        }

        private async Task SaveScheduleAsync()
        {
            var current = env.Store.LoadSettings();
            var settings = current.Clone();
            settings.ScheduleEnabled = ScheduleEnabled;
            settings.Frequency = frequency;
            settings.DayOfWeek = DayOfWeek;
            settings.MonthlyDay = MonthlyDay;
            settings.Time = (Time ?? string.Empty).Trim();
            settings.RunIfMissed = RunIfMissed;
            settings.RunAtStartup = RunAtStartup && frequency != Frequencies.Startup;
            settings.WakeToRun = WakeToRun;
            settings.AutoRetryEnabled = AutoRetry;
            settings.ShowRebootPrompt = RebootPrompt;
            settings.InstallFirmwareUpdates = InstallFirmware;

            var errors = settings.Validate();
            if (errors.Count > 0)
            {
                SetScheduleMessage(() => T["WU_Schedule_Invalid"], true);
                return;
            }

            if (settings.InstallFirmwareUpdates && !current.InstallFirmwareUpdates && !Dialogs.Confirm(T["WU_Firmware_Schedule_Warning"])) return;

            // D-027: portable scheduling offers MSI installation or a protected local task copy.
            if (settings.ScheduleEnabled && IsPortableCopy())
            {
                var choice = Dialogs.Choose(T.Format("WU_Schedule_InstallOffer", env.ExecutablePath));
                if (choice == System.Windows.MessageBoxResult.Cancel) return;
                if (choice == System.Windows.MessageBoxResult.Yes)
                {
                    if (InstallForSchedule != null)
                    {
                        if (await InstallForSchedule(MsiScheduleProperties.CommandLine(settings))) return;
                    }
                    SetScheduleMessage(() => T["WU_Schedule_TargetUnavailable"], true);
                    return;
                }
            }

            try
            {
                await Task.Run(() => new ScheduleService(env.Tasks, env.Log, env.ExecutablePath, env.Clock).Apply(settings));
                env.Store.SaveSettings(settings);
                SetScheduleMessage(() => settings.ScheduleEnabled ? T["WU_Schedule_Saved"] : T["WU_Schedule_Off"], false);
            }
            catch (WinTool.Core.Updates.InstalledCopyRequiredException)
            { SetScheduleMessage(() => T["WU_Schedule_TargetUnavailable"], true); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is UnauthorizedAccessException || ex is System.Runtime.InteropServices.COMException || ex is ArgumentException)
            {
                SetScheduleMessage(() => T.Format("WU_Schedule_Failed", ex.Message), true);
            }

            await RefreshNextRunAsync(env.Store.LoadSettings());
        }

        private async Task RefreshNextRunAsync(WindowsUpdateSettings settings)
        {
            if (!settings.ScheduleEnabled)
            {
                NextRun = T["WU_Schedule_NotScheduled"];
                return;
            }

            try
            {
                var status = await Task.Run(() => new ScheduleService(env.Tasks, env.Log, env.ExecutablePath, env.Clock).GetStatus(settings));
                if (status.Main == null) NextRun = T["WU_Schedule_TaskMissing"];
                else NextRun = status.Main.NextRunTime.HasValue ? status.Main.NextRunTime.Value.ToString("g", T.Culture) : T["WU_Schedule_AtStartup"];
                if (status.MatchesSettings == false) SetScheduleMessage(() => T["WU_Schedule_Mismatch"], true);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException || ex is UnauthorizedAccessException)
            {
                NextRun = ex.Message;
            }
        }

        private void SetFrequency(string value)
        {
            frequency = Array.IndexOf(Frequencies.All, value) >= 0 ? value : Frequencies.Monthly;
            foreach (var name in new[] { nameof(IsDaily), nameof(IsWeekly), nameof(IsMonthly), nameof(IsStartup), nameof(ShowDay), nameof(ShowMonthDay), nameof(ShowTime), nameof(ShowRunAtStartup) })
            {
                OnPropertyChanged(name);
            }
        }

        // ------------------------------------------------------------- helpers

        private async Task RefreshStateAsync()
        {
            pending = await Task.Run(() => env.Probe.GetPendingRestart());
            LegacyDetected = await Task.Run(() =>
            {
                try
                {
                    return env.ReadLegacyConfig() != null || env.Tasks.Find(TaskNames.IsLegacyWinUpdate).Count > 0 ||
                           env.LegacyInstallations.Find().Count > 0;
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException)) { return false; }
            });
            RaiseSummary();
            await HealthAsync(false);
        }

        private void ApplyScan(ScanResult scan)
        {
            Rows.Clear();
            foreach (var update in scan.Updates.Where(u => u.Excluded != ExclusionReason.Scope)) Rows.Add(new UpdateRowViewModel(update));
            hasScanned = true;
            lastScan = env.Clock();
            RaiseSummary();
        }

        private void OnProgress(RunProgress value)
        {
            runStage = value.Stage;
            OnPropertyChanged(nameof(CancelText));
            switch (value.Stage)
            {
                case RunStage.Searching:
                    ProgressIndeterminate = true;
                    ProgressText = T.Format("WU_Progress_SearchingPass", value.Pass);
                    break;
                case RunStage.Downloading:
                    ProgressIndeterminate = false;
                    ProgressValue = value.Total == 0 ? 0 : 100.0 * (value.Current - 1) / value.Total;
                    ProgressText = T.Format("WU_Progress_Downloading", value.Current, value.Total, value.Title);
                    break;
                case RunStage.Installing:
                    ProgressIndeterminate = true;
                    ProgressText = T.Format("WU_Progress_Installing", value.Total);
                    break;
                case RunStage.Verifying:
                    ProgressIndeterminate = true;
                    ProgressText = T["WU_Progress_Verifying"];
                    break;
            }
        }

        private string Explain(RunOutcome outcome)
        {
            switch (outcome.Result)
            {
                case RunResult.Succeeded: return T["WU_Result_Succeeded"];
                case RunResult.SucceededWithErrors: return T["WU_Result_SomeFailed"];
                case RunResult.NoUpdates: return T["WU_NoUpdates"];
                case RunResult.RebootRequired:
                    return outcome.Items.Any(i => i.Succeeded) ? T["WU_Result_Restart"] : T["WU_Result_RestartBlocking"];
                case RunResult.Cancelled: return T["WU_Result_Cancelled"];
                case RunResult.AlreadyRunning: return T["WU_OperationRunning"];
                default: return T.Format("WU_Result_Other", outcome.Message);
            }
        }

        private string HealthText(HealthItem item)
        {
            var key = "WU_HealthItem_" + item.Id.Replace("-", "_") + "_" + item.Status;
            var text = T[key];
            if (text.StartsWith("!", StringComparison.Ordinal)) return item.Detail;
            return item.FreeGigabytes.HasValue
                ? text + " (" + T.Format("WU_Health_FreeSpace", item.FreeGigabytes.Value.ToString("N1", T.Culture)) + ")"
                : text;
        }

        private bool IsPortableCopy()
        {
            return !WinTool.Core.Updates.InstalledCopy.IsInstalled(env.ExecutablePath);
        }

        private bool Begin(string text, bool indeterminate)
        {
            if (busy) return false;
            runStage = RunStage.Checking;
            OnPropertyChanged(nameof(CancelText));
            IsBusy = true;
            ProgressText = text;
            ProgressIndeterminate = indeterminate;
            ProgressValue = 0;
            SetMessage(null, false);
            return true;
        }

        private void End()
        {
            IsBusy = false;
            ProgressText = null;
            CommandManager.InvalidateRequerySuggested();
        }

        private void SetMessage(Func<string> text, bool isError)
        {
            MessageIsError = isError;
            message = text;
            OnPropertyChanged(nameof(Message));
            OnPropertyChanged(nameof(HasMessage));
        }

        private void SetScheduleMessage(Func<string> text, bool isError)
        {
            ScheduleMessageIsError = isError;
            scheduleMessage = text;
            OnPropertyChanged(nameof(ScheduleMessage));
            OnPropertyChanged(nameof(HasScheduleMessage));
        }

        private void RaiseSummary()
        {
            foreach (var name in new[] { nameof(HasScanned), nameof(HasUpdates), nameof(NoUpdatesFound), nameof(ImportantCount), nameof(OtherCount),
                nameof(LastCheckText), nameof(CountText), nameof(RestartPending), nameof(NoRestartPending), nameof(RestartReason) })
            {
                OnPropertyChanged(name);
            }

            CommandManager.InvalidateRequerySuggested();
            SummaryChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
