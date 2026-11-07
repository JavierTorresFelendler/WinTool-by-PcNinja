using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using WinTool.Core;
using WinTool.Core.Machine;
using WinTool.Upgrade;
using WinTool.WindowsUpdate.Engine;
using WinTool.WindowsUpdate.Schedule;
using WinTool.WindowsUpdate.Storage;

namespace WinTool.App.ViewModels
{
    /// <summary>
    /// Read-only summary of what each tool sees on this computer, with a way
    /// to each tool (owner decision). Nothing here changes the machine.
    /// </summary>
    public sealed class DashboardPageViewModel : PageViewModel
    {
        private readonly IWindowsUpdateEnvironment env;
        private readonly WindowsUpdatePageViewModel windowsUpdate;
        private readonly WindowsUpgradePageViewModel upgrade;
        private readonly OfficePageViewModel officePage;
        private UpgradeAttempt lastUpgrade;
        private MachineInfo machine;
        private OfficeInfo office;
        private bool officeChecked;
        private string wan;
        private WindowsUpdateState state = new WindowsUpdateState();
        private WindowsUpdateSettings settings = new WindowsUpdateSettings();
        private bool loading;

        public DashboardPageViewModel(IWindowsUpdateEnvironment env, WindowsUpdatePageViewModel windowsUpdate,
            WindowsUpgradePageViewModel upgrade, OfficePageViewModel officePage, Action<string> navigate)
            : base("Nav_Dashboard", "Page_Dashboard_Subtitle")
        {
            this.env = env;
            this.windowsUpdate = windowsUpdate;
            this.upgrade = upgrade;
            this.officePage = officePage;
            windowsUpdate.SummaryChanged += (sender, e) => Refresh();
            // Running operations and their results show here as soon as they change (owner test 2026-11-06).
            upgrade.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName != nameof(WindowsUpgradePageViewModel.IsBusy)) return;
                if (!upgrade.IsBusy) _ = ReloadUpgradeAsync();
                OnPropertyChanged(nameof(RunningOperation));
                OnPropertyChanged(nameof(UpgradeStatusText));
                OnPropertyChanged(nameof(HasUpgradeStatus));
            };
            officePage.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName != nameof(OfficePageViewModel.IsBusy)) return;
                if (!officePage.IsBusy) _ = ReloadOfficeAsync();
                OnPropertyChanged(nameof(RunningOperation));
            };
            RefreshCommand = new AsyncCommand(LoadAsync, () => !loading);
            OpenWindowsUpdateCommand = new RelayCommand(() => navigate("windows-update"));
            OpenUpgradeCommand = new RelayCommand(() => navigate("windows-upgrade"));
            OpenOfficeCommand = new RelayCommand(() => navigate("office"));
        }

        public ICommand RefreshCommand { get; }
        public ICommand OpenWindowsUpdateCommand { get; }
        public ICommand OpenUpgradeCommand { get; }
        public ICommand OpenOfficeCommand { get; }

        // Windows Update card
        public string UpdatesText => windowsUpdate.CountText;
        public bool RestartPending => windowsUpdate.RestartPending;
        public string ScheduleText => WinTool.App.Localization.ScheduleWords.Describe(settings, T);
        public string LastRunText => string.IsNullOrEmpty(state.LastRunFinished)
            ? T["Dash_Never"]
            : FormatStamp(state.LastRunFinished) + " · " + (state.LastResult ?? string.Empty);

        // Windows card
        public string WindowsName => machine?.Os?.FriendlyName ?? string.Empty;
        public string WindowsBuild => machine?.Os == null ? string.Empty : T.Format("Dash_Build", machine.Os.Build + "." + machine.Os.Revision);
        public bool IsWindows10 => machine?.Os != null && MachineInfo.IsWindows10(machine.Os.Build);

        /// <summary>"Upgrade running", or the last upgrade attempt and when it started.</summary>
        public string UpgradeStatusText
        {
            get
            {
                if (upgrade.IsBusy) return T["Dash_UpgradeRunning"];
                if (lastUpgrade == null) return string.Empty;
                string state;
                switch (lastUpgrade.State)
                {
                    case "SetupStarting":
                    case "PreparationCompleted":
                    case "OutcomeUnconfirmed":
                    case "RollbackObserved":
                    case "TargetWindowsObserved":
                        state = T["Dash_Upgrade_" + lastUpgrade.State];
                        break;
                    default:
                        state = lastUpgrade.State;
                        break;
                }

                return T.Format("Dash_UpgradeLast", state, lastUpgrade.StartedUtc.ToLocalTime().ToString("g", T.Culture));
            }
        }

        public bool HasUpgradeStatus => upgrade.IsBusy || lastUpgrade != null;
        public bool UpgradeStatusIsWarning => !upgrade.IsBusy && lastUpgrade != null &&
            (lastUpgrade.State == "RollbackObserved" || lastUpgrade.State == "OutcomeUnconfirmed");

        // Office card
        public bool HasOffice => office != null;
        public bool NoOffice => officeChecked && office == null;
        public string OfficeName => office?.ProductName ?? string.Empty;
        public string OfficeDetails => office == null ? string.Empty
            : string.Join(" · ", new[] { "Click-to-Run", office.Platform, office.Channel ?? (office.IsVolumeLicense ? T["Dash_VolumeLicense"] : null) }.Where(s => !string.IsNullOrEmpty(s)));
        public bool OfficeOutOfSupport => office != null && office.OutOfSupport;
        public string OfficeApps => office == null ? string.Empty : string.Join("  ·  ", office.Apps);

        // This computer
        public string ComputerName => machine?.ComputerName ?? Environment.MachineName;
        public string UserName => machine?.UserName ?? string.Empty;
        public string LanAddress => machine?.LanAddress ?? T["Dash_NotConnected"];
        public string WanAddress => wan ?? T["Dash_Checking"];
        public string Processor => machine?.Processor == null ? string.Empty : machine.Processor + " · " + T.Format("Dash_Threads", machine.LogicalProcessors);
        public string Memory => MachineInfo.FormatBytes(machine?.MemoryBytes);
        public string Storage => machine?.SystemDriveTotalBytes == null ? string.Empty
            : T.Format("Dash_Storage", MachineInfo.FormatBytes(machine.SystemDriveTotalBytes), MachineInfo.FormatBytes(machine.SystemDriveFreeBytes));
        public string Graphics => machine?.Graphics ?? string.Empty;
        public string Motherboard => machine?.Motherboard ?? string.Empty;
        public string Bios => machine?.Bios ?? string.Empty;
        public string SecureBoot => machine?.SecureBoot == null ? string.Empty : machine.SecureBoot.Value ? T["Dash_On"] : T["Dash_Off"];
        public string Uptime => machine?.Uptime == null ? string.Empty : FormatUptime(machine.Uptime.Value);

        // WinTool card
        public string ToolVersion => ProductInfo.Version;
        public string InstallType => env.ExecutablePath.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase)
            ? T["Dash_Installed"] : T["Dash_Portable"];
        public string RunningOperation
        {
            get
            {
                var running = new[]
                {
                    windowsUpdate.IsBusy ? T["Nav_WindowsUpdate"] : null,
                    upgrade.IsBusy ? T["Nav_WindowsUpgrade"] : null,
                    officePage.IsBusy ? T["Nav_Office"] : null
                }.Where(name => name != null).ToList();
                return running.Count == 0 ? T["Dash_None"] : string.Join(", ", running);
            }
        }

        public override void OnActivated()
        {
            if (machine == null && !loading) RefreshCommand.Execute(null);
            else Refresh();
        }

        private async Task LoadAsync()
        {
            loading = true;
            try
            {
                settings = env.Store.LoadSettings();
                state = env.Store.LoadState();
                machine = await Task.Run(() => MachineInfo.ReadCurrent());
                office = await Task.Run(() =>
                {
                    try { return OfficeInfo.ReadCurrent(); }
                    catch (Exception ex) when (!(ex is OutOfMemoryException)) { return null; }
                });
                officeChecked = true;
                lastUpgrade = await Task.Run(() => ReadUpgrade());
                Refresh();
                wan = await Task.Run(() => MachineInfo.ReadWanAddress()) ?? T["Dash_NotConnected"];
                Refresh();
            }
            finally
            {
                loading = false;
            }
        }

        private static UpgradeAttempt ReadUpgrade()
        {
            try { return UpgradeHistory.ReadStatus(); }
            catch (Exception ex) when (!(ex is OutOfMemoryException)) { return null; }
        }

        private async Task ReloadUpgradeAsync()
        {
            lastUpgrade = await Task.Run(() => ReadUpgrade());
            Refresh();
        }

        private async Task ReloadOfficeAsync()
        {
            office = await Task.Run(() =>
            {
                try { return OfficeInfo.ReadCurrent(); }
                catch (Exception ex) when (!(ex is OutOfMemoryException)) { return null; }
            });
            officeChecked = true;
            Refresh();
        }

        private void Refresh()
        {
            settings = env.Store.LoadSettings();
            state = env.Store.LoadState();
            OnPropertyChanged(string.Empty);
        }

        private string FormatStamp(string stamp)
        {
            var parsed = WindowsUpdateState.Parse(stamp);
            return parsed.HasValue ? parsed.Value.ToString("g", T.Culture) : stamp;
        }

        private string FormatUptime(TimeSpan uptime)
        {
            if (uptime.TotalDays >= 1) return T.Format("Dash_DaysAgo", (int)uptime.TotalDays);
            if (uptime.TotalHours >= 1) return T.Format("Dash_HoursAgo", (int)uptime.TotalHours);
            return T.Format("Dash_MinutesAgo", Math.Max(1, (int)uptime.TotalMinutes));
        }
    }
}
