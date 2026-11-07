using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;
using WinTool.Cli;
using WinTool.Core.Diagnostics;
using WinTool.Core.Json;
using WinTool.WindowsUpdate.Engine;

namespace WinTool.App.ViewModels
{
    /// <summary>
    /// WinTool's log, read only while this page is open (mockup): filter by area,
    /// choose the day, follow new lines, collect a log package, open the folder.
    /// </summary>
    public sealed class LogsPageViewModel : PageViewModel
    {
        public const int MaxEntries = 2000;

        private readonly IWindowsUpdateEnvironment env;
        private readonly DispatcherTimer timer;
        private string filter = "All";
        private DateTime? day;
        private bool follow;
        private bool active;
        private int total;
        private Func<string> message;
        private bool messageIsError;
        private bool collecting;
        private int reloadVersion;

        private readonly WinTool.Core.Settings.AppSettings settings;
        private readonly Action save;

        public LogsPageViewModel(IWindowsUpdateEnvironment env, WinTool.Core.Settings.AppSettings settings, Action save)
            : base("Nav_Logs", "Page_Logs_Subtitle")
        {
            this.env = env;
            this.settings = settings;
            this.save = save;
            follow = settings?.LogsFollow ?? true;
            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (sender, e) => _ = ReloadAsync();
            CollectCommand = new RelayCommand(() => _ = CollectAsync(), () => !collecting);
            OpenFolderCommand = new RelayCommand(OpenFolder);
        }

        public ICommand CollectCommand { get; }
        public ICommand OpenFolderCommand { get; }

        public ObservableCollection<LogEntry> Entries { get; } = new ObservableCollection<LogEntry>();
        public ObservableCollection<DateTime> Days { get; } = new ObservableCollection<DateTime>();

        /// <summary>Raised after new entries arrive while following, so the view can scroll to the end.</summary>
        public event EventHandler FollowedToEnd;

        public bool IsAll { get => filter == "All"; set { if (value) SetFilter("All"); } }
        public bool IsWindowsUpdate { get => filter == "WindowsUpdate"; set { if (value) SetFilter("WindowsUpdate"); } }
        public bool IsUpgrade { get => filter == "Upgrade"; set { if (value) SetFilter("Upgrade"); } }
        public bool IsOffice { get => filter == "Office"; set { if (value) SetFilter("Office"); } }

        public DateTime? Day
        {
            get => day;
            set { if (Set(ref day, value)) _ = ReloadAsync(); }
        }

        public bool Follow
        {
            get => follow;
            set
            {
                if (!Set(ref follow, value)) return;
                if (settings != null)
                {
                    settings.LogsFollow = value;
                    save?.Invoke();
                }

                UpdateTimer();
                if (value) _ = ReloadAsync();
            }
        }

        public string CountText => total > MaxEntries ? T.Format("LG_Shown", total, MaxEntries) : T.Format("LG_Count", Entries.Count);
        public bool IsEmpty => Entries.Count == 0;
        public string Message => message?.Invoke() ?? string.Empty;
        public bool HasMessage => !string.IsNullOrEmpty(Message);

        private void SetMessage(Func<string> text, bool isError)
        {
            message = text;
            MessageIsError = isError;
            OnPropertyChanged(nameof(Message));
            OnPropertyChanged(nameof(HasMessage));
        }
        public bool MessageIsError { get => messageIsError; private set => Set(ref messageIsError, value); }

        public override bool FillsWindow => true;

        public override void OnActivated()
        {
            active = true;
            LoadDays();
            UpdateTimer();
            _ = ReloadAsync();
        }

        public override void OnDeactivated()
        {
            // Logs are read only while the page is open.
            active = false;
            UpdateTimer();
            Entries.Clear();
        }

        /// <summary>Reloads now and completes when the list shows the result (UI smoke screenshots).</summary>
        internal Task ReloadForCaptureAsync() => ReloadAsync();

        private static bool SameEntry(LogEntry a, LogEntry b) =>
            a == null ? b == null : b != null && a.Time == b.Time && a.Area == b.Area && a.Message == b.Message;

        private void UpdateTimer()
        {
            if (active && follow) timer.Start();
            else timer.Stop();
        }

        private void SetFilter(string value)
        {
            if (filter == value) return;
            filter = value;
            OnPropertyChanged(nameof(IsAll));
            OnPropertyChanged(nameof(IsWindowsUpdate));
            OnPropertyChanged(nameof(IsUpgrade));
            OnPropertyChanged(nameof(IsOffice));
            _ = ReloadAsync();
        }

        private void LoadDays()
        {
            var keep = day;
            Days.Clear();
            foreach (var d in LogReader.Days(AppLog.Current.Directory)) Days.Add(d);
            var today = DateTime.Today;
            if (!Days.Contains(today)) Days.Insert(0, today);
            day = keep.HasValue && Days.Contains(keep.Value) ? keep : Days[0];
            OnPropertyChanged(nameof(Day));
        }

        private async Task ReloadAsync()
        {
            if (!active || !day.HasValue) return;
            // Only the newest reload may fill the list (filter, day and Follow can overlap).
            var version = ++reloadVersion;
            var path = LogReader.PathFor(AppLog.Current.Directory, day.Value);
            var area = filter;
            IReadOnlyList<LogEntry> entries;
            try
            {
                entries = await Task.Run(() => LogReader.ReadTail(path, area == "All" ? MaxEntries : MaxEntries * 10)
                    .Where(e => area == "All" || string.Equals(e.Area, area, StringComparison.OrdinalIgnoreCase)).ToList());
            }
            catch (IOException ex)
            {
                var error = ex.Message;
                SetMessage(() => error, true);
                return;
            }

            if (!active || version != reloadVersion) return;
            // New lines also count when the list is already full (the count stays at MaxEntries).
            var last = Entries.Count == 0 ? null : Entries[Entries.Count - 1];
            var newest = entries.Count == 0 ? null : entries[entries.Count - 1];
            var grew = entries.Count != total || !SameEntry(last, newest);
            total = entries.Count;
            Entries.Clear();
            foreach (var entry in entries.Skip(Math.Max(0, entries.Count - MaxEntries))) Entries.Add(entry);
            OnPropertyChanged(nameof(CountText));
            OnPropertyChanged(nameof(IsEmpty));
            if (follow && grew) FollowedToEnd?.Invoke(this, EventArgs.Empty);
        }

        private async Task CollectAsync()
        {
            collecting = true;
            CommandManager.InvalidateRequerySuggested();
            try
            {
                var output = new StringWriter();
                var code = await Task.Run(() => new CommandRunner(env, output, output).Run(new[] { "logs", "collect", "--json" }));
                string zip = null;
                try
                {
                    var json = JsonData.Parse(output.ToString().Trim()) as Dictionary<string, object>;
                    object value;
                    if (json != null && json.TryGetValue("zipPath", out value)) zip = value as string;
                }
                catch (FormatException) { }

                if (code == 0 && zip != null)
                {
                    SetMessage(() => T.Format("LG_Collected", zip), false);
                    Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + zip + "\"") { UseShellExecute = true });
                }
                else
                {
                    var details = output.ToString().Trim();
                    SetMessage(() => T["LG_CollectFailed"] + " " + details, true);
                }
            }
            finally
            {
                collecting = false;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void OpenFolder()
        {
            try
            {
                Directory.CreateDirectory(AppLog.Current.Directory);
                Process.Start(new ProcessStartInfo(AppLog.Current.Directory) { UseShellExecute = true });
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                var error = ex.Message;
                SetMessage(() => error, true);
            }
        }
    }
}
