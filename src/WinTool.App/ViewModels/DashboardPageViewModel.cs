using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using WinTool.Core;
using WinTool.Core.Machine;
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
        private MachineInfo machine;
        private OfficeInfo office;
        private bool officeChecked;
        private string wan;
        private WindowsUpdateState state = new WindowsUpdateState();
        private WindowsUpdateSettings settings = new WindowsUpdateSettings();
        private bool loading;

        public DashboardPageViewModel(IWindowsUpdateEnvironment env, WindowsUpdatePageViewModel windowsUpdate, Action<string> navigate)
            : base("Nav_Dashboard", "Page_Dashboard_Subtitle")
        {
            this.env = env;
            this.windowsUpdate = windowsUpdate;
            windowsUpdate.SummaryChanged += (sender, e) => Refresh();
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
        public string ScheduleText => settings.ScheduleEnabled ? ScheduleService.Describe(settings) : T["Dash_ScheduleOff"];
        public string LastRunText => string.IsNullOrEmpty(state.LastRunFinished)
            ? T["Dash_Never"]
            : FormatStamp(state.LastRunFinished) + " · " + (state.LastResult ?? string.Empty);

        // Windows card
        public string WindowsName => machine?.Os?.FriendlyName ?? string.Empty;
        public string WindowsBuild => machine?.Os == null ? string.Empty : T.Format("Dash_Build", machine.Os.Build + "." + machine.Os.Revision);
        public bool IsWindows10 => machine?.Os != null && MachineInfo.IsWindows10(machine.Os.Build);

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
        public string RunningOperation => windowsUpdate.IsBusy ? T["Nav_WindowsUpdate"] : T["Dash_None"];

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
                Refresh();
                wan = await Task.Run(() => MachineInfo.ReadWanAddress()) ?? T["Dash_NotConnected"];
                Refresh();
            }
            finally
            {
                loading = false;
            }
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
