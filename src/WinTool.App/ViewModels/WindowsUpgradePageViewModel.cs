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
using WinTool.App.Infrastructure;
using WinTool.Core.Machine;
using WinTool.Core.Operations;
using WinTool.Upgrade;
using UpgradeLog = WinTool.Upgrade.AppLog;

namespace WinTool.App.ViewModels
{
    /// <summary>A download destination: a drive found by Core, or a folder the user chose.</summary>
    public sealed class DownloadDestination
    {
        public DownloadDestination(string folder, string display)
        {
            Folder = folder;
            Display = display;
        }

        public string Folder { get; }
        public string Display { get; }
        public override string ToString() => Display;
    }

    /// <summary>
    /// The Windows Upgrade screen: the Windows Upgrade Tool's window flow on the
    /// same Core operations as "upgrade" on the command line (WINUPGRADE-BEHAVIOR).
    /// Core messages are English; they appear as details under a translated headline.
    /// </summary>
    public sealed class WindowsUpgradePageViewModel : PageViewModel
    {
        private const int SetupTransitionExitCode = 0x40010004;

        private readonly UpgradeEngine engine = new UpgradeEngine();
        private DownloadSystem system;
        private CancellationTokenSource operation;
        private bool loaded;
        private bool loading;
        private bool setupStarted;
        private MountedSource preparedSource;
        private string preparedKey;
        private DownloadDestination chosenFolder;
        private bool restartBeforeQuiet = true;

        public WindowsUpgradePageViewModel()
            : base("Nav_WindowsUpgrade", "Page_WindowsUpgrade_Subtitle")
        {
            ValidateCommand = new RelayCommand(() => Run(() => PrepareOrUpgradeAsync(false)), () => CanUseSources && !UseDownload && HasSourcePath);
            StartCommand = new RelayCommand(StartOrCancel, () => !setupStarted && (IsBusy ? !IsCancelling : CanStart));
            CopyCommandCommand = new RelayCommand(() => Run(CopyServerCommandAsync), () => CanUseSources && SelectedServerImageIsUsable);
            FindIsoCommand = new RelayCommand(() => Run(FindMatchingDownloadAsync), () => CanUseSources);
            OpenPcNinjaCommand = new RelayCommand(() => OpenPage(DownloadSourcePolicy.GetPcNinjaBrowserPage(system)), () => system != null && !IsBusy);
            OpenMicrosoftCommand = new RelayCommand(() => OpenPage(UpgradeEngine.MicrosoftDownloadPage), () => !IsBusy);
            BrowseIsoCommand = new RelayCommand(BrowseIso, () => CanUseSources);
            BrowseFolderCommand = new RelayCommand(BrowseFolder, () => CanUseSources);
            ChooseDownloadFolderCommand = new RelayCommand(ChooseDownloadFolder, () => CanUseSources && AutomaticDownloadAvailable);
            RefreshCommand = new RelayCommand(() => Run(() => RefreshSourcesAsync(true)), () => CanUseSources);
            ViewLogCommand = new RelayCommand(() => OpenPage(UpgradeLog.Path));
        }

        public ICommand ValidateCommand { get; }
        public ICommand StartCommand { get; }
        public ICommand CopyCommandCommand { get; }
        public ICommand FindIsoCommand { get; }
        public ICommand OpenPcNinjaCommand { get; }
        public ICommand OpenMicrosoftCommand { get; }
        public ICommand BrowseIsoCommand { get; }
        public ICommand BrowseFolderCommand { get; }
        public ICommand ChooseDownloadFolderCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand ViewLogCommand { get; }

        public ObservableCollection<string> Sources { get; } = new ObservableCollection<string>();
        public ObservableCollection<DownloadDestination> Destinations { get; } = new ObservableCollection<DownloadDestination>();
        public ObservableCollection<InstallationImage> Images { get; } = new ObservableCollection<InstallationImage>();

        // ---------------------------------------------------------------- this computer

        private string windowsText, processorText, memoryText, freeSpaceText, hostBlock, previousText;
        private bool? secureBoot;

        public string WindowsText { get => windowsText; private set => Set(ref windowsText, value); }
        public string ProcessorText { get => processorText; private set => Set(ref processorText, value); }
        public string MemoryText { get => memoryText; private set => Set(ref memoryText, value); }
        public string FreeSpaceText { get => freeSpaceText; private set => Set(ref freeSpaceText, value); }
        public string SecureBootText => secureBoot == null ? string.Empty : secureBoot.Value ? T["Dash_On"] : T["Dash_Off"];

        /// <summary>Core's reason why an upgrade cannot run here (English), or null.</summary>
        public string HostBlock
        {
            get => hostBlock;
            private set
            {
                if (Set(ref hostBlock, value)) OnPropertyChanged(string.Empty);
            }
        }

        public bool IsServer => system != null && system.IsServer;
        public bool IsClient => !IsServer;
        public bool UpgradeAllowed => system != null && !IsServer && hostBlock == null;
        public bool HasHostBlock => hostBlock != null;

        public string EligibilityText => system == null ? string.Empty
            : IsServer ? T["UP_ServerGuidance"]
            : UpgradeAllowed ? T["UP_Eligible"] : T["UP_Blocked"];

        public string NextActionText
        {
            get
            {
                if (system == null) return string.Empty;
                if (IsServer) return T["UP_Next_Server"];
                if (!UpgradeAllowed) return T["UP_Next_Blocked"];
                if (preparedSource != null) return SelectedImage != null ? T["UP_Next_Start"] : T["UP_Next_Image"];
                return UseDownload ? T["UP_Next_Download"] : T["UP_Next_Media"];
            }
        }

        public string PreviousText { get => previousText; private set => Set(ref previousText, value); }
        public bool HasPrevious => !string.IsNullOrEmpty(previousText);

        // ---------------------------------------------------------------- source

        private bool useDownload;
        private bool automaticDownloadAvailable;
        private string sourcePath = string.Empty;
        private DownloadDestination selectedDestination;

        public bool AutomaticDownloadAvailable
        {
            get => automaticDownloadAvailable;
            private set => Set(ref automaticDownloadAvailable, value);
        }

        public string DownloadHint => system == null ? string.Empty
            : AutomaticDownloadAvailable ? T.Format("UP_SrcDownloadHint", system.Language) : T["UP_SrcDownloadUnavailable"];

        public bool UseDownload
        {
            get => useDownload;
            set
            {
                if (value && !AutomaticDownloadAvailable) value = false;
                if (Set(ref useDownload, value))
                {
                    OnPropertyChanged(nameof(UseExisting));
                    OnPropertyChanged(nameof(NextActionText));
                }
            }
        }

        public bool UseExisting
        {
            get => !useDownload;
            set => UseDownload = !value;
        }

        public string SourcePath
        {
            get => sourcePath;
            set
            {
                if (!Set(ref sourcePath, value ?? string.Empty)) return;
                OnPropertyChanged(nameof(HasSourcePath));
                OnPropertyChanged(nameof(SelectedFoundSource));
                if (!IsBusy) UseDownload = false;
                if (preparedKey != null && !string.Equals(CleanPath(sourcePath), preparedKey, StringComparison.OrdinalIgnoreCase))
                {
                    ClearPrepared();
                    SetStatus(() => T["UP_Status_SourceChanged"]);
                }
            }
        }

        public bool HasSourcePath => !string.IsNullOrWhiteSpace(sourcePath);

        public bool HasFoundSources => Sources.Count > 0;

        /// <summary>A found ISO or mounted drive; choosing one fills the path.</summary>
        public string SelectedFoundSource
        {
            get => Sources.FirstOrDefault(item => string.Equals(item, CleanPath(sourcePath), StringComparison.OrdinalIgnoreCase));
            set
            {
                if (value != null) SourcePath = value;
            }
        }

        public DownloadDestination SelectedDestination
        {
            get => selectedDestination;
            set => Set(ref selectedDestination, value);
        }

        // ---------------------------------------------------------------- image and options

        private InstallationImage selectedImage;
        private string imageInfo;
        private bool imageInfoIsWarning;
        private bool quiet;
        private bool autoRestart = true;

        public InstallationImage SelectedImage
        {
            get => selectedImage;
            set
            {
                if (!Set(ref selectedImage, value)) return;
                UpdateImageInfo();
                OnPropertyChanged(nameof(NextActionText));
            }
        }

        public bool HasImages => Images.Count > 0;
        public bool ShowImagePrompt => !HasImages;
        public string ImagePrompt => T["UP_ImageValidateFirst"];

        /// <summary>Core's image details and reasons (English).</summary>
        public string ImageInfo { get => imageInfo; private set { Set(ref imageInfo, value); OnPropertyChanged(nameof(HasImageInfo)); } }
        public bool HasImageInfo => !string.IsNullOrEmpty(imageInfo);
        public bool ImageInfoIsWarning { get => imageInfoIsWarning; private set => Set(ref imageInfoIsWarning, value); }

        public bool Quiet
        {
            get => quiet;
            set
            {
                if (!Set(ref quiet, value)) return;
                // Silent Setup restarts by itself; the choice comes back when silent mode is turned off.
                if (value)
                {
                    restartBeforeQuiet = autoRestart;
                    AutoRestart = true;
                }
                else AutoRestart = restartBeforeQuiet;
                OnPropertyChanged(nameof(CanChangeRestart));
            }
        }

        public bool AutoRestart { get => autoRestart; set => Set(ref autoRestart, value); }
        public bool CanChangeRestart => !quiet && !IsBusy && UpgradeAllowed;
        public bool CanChangeOptions => !IsBusy && UpgradeAllowed;

        // ---------------------------------------------------------------- status

        private bool isBusy;
        private bool isCancelling;
        private Func<string> status;
        private string statusDetail;
        private Func<string> notice;
        private string noticeDetail;
        private bool noticeIsWarning;
        private double progressValue;
        private bool progressIndeterminate = true;

        public bool IsBusy
        {
            get => isBusy;
            private set
            {
                if (!Set(ref isBusy, value)) return;
                OnPropertyChanged(nameof(CanUseSources));
                OnPropertyChanged(nameof(CanChangeRestart));
                OnPropertyChanged(nameof(CanChangeOptions));
                OnPropertyChanged(nameof(StartLabel));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsCancelling { get => isCancelling; private set { Set(ref isCancelling, value); OnPropertyChanged(nameof(StartLabel)); } }
        public bool CanUseSources => !IsBusy && system != null;
        public bool CanStart => UpgradeAllowed && (UseDownload ? SelectedDestination != null : HasSourcePath);

        public string StartLabel => setupStarted ? T["UP_SetupRunning"]
            : IsCancelling ? T["UP_Status_Cancelling"]
            : IsBusy ? T["UP_Cancel"] : T["UP_Start"];

        public string Status => status?.Invoke() ?? string.Empty;

        /// <summary>Core's current stage (English).</summary>
        public string StatusDetail { get => statusDetail; private set { Set(ref statusDetail, value); OnPropertyChanged(nameof(HasStatusDetail)); } }
        public bool HasStatusDetail => !string.IsNullOrEmpty(statusDetail);

        public string Notice => notice?.Invoke() ?? string.Empty;
        public bool HasNotice => notice != null;

        /// <summary>Core's message under the notice (English).</summary>
        public string NoticeDetail { get => noticeDetail; private set { Set(ref noticeDetail, value); OnPropertyChanged(nameof(HasNoticeDetail)); } }
        public bool HasNoticeDetail => !string.IsNullOrEmpty(noticeDetail);
        public bool NoticeIsWarning { get => noticeIsWarning; private set => Set(ref noticeIsWarning, value); }

        public double ProgressValue { get => progressValue; private set => Set(ref progressValue, value); }
        public bool ProgressIndeterminate { get => progressIndeterminate; private set => Set(ref progressIndeterminate, value); }

        // ---------------------------------------------------------------- loading

        public override void OnActivated()
        {
            if (!loaded && !loading) Run(LoadAsync);
        }

        private async Task LoadAsync()
        {
            loading = true;
            SetStatus(() => T["UP_Status_Reading"]);
            try
            {
                var facts = await Task.Run(() => new
                {
                    System = DownloadSystem.Read(),
                    Os = engine.GetOperatingSystemDescription(),
                    Machine = MachineInfo.ReadCurrent(),
                    Storage = engine.CheckSystemDriveSpace(),
                    Previous = UpgradeHistory.ReadStatus()
                });
                system = facts.System;
                WindowsText = facts.Os;
                ProcessorText = facts.Machine.Processor ?? string.Empty;
                MemoryText = MachineInfo.FormatBytes(facts.Machine.MemoryBytes);
                FreeSpaceText = facts.Storage.RootPath + " " + MachineInfo.FormatBytes(facts.Storage.AvailableBytes);
                secureBoot = facts.Machine.SecureBoot;
                PreviousText = facts.Previous == null ? null : facts.Previous.State + ". " + facts.Previous.Message;
                OnPropertyChanged(nameof(HasPrevious));
                AutomaticDownloadAvailable = AutomaticDownloadPolicy.IsAvailable(system);
                HostBlock = ManualUpgradePolicy.GetUnavailableReason(system);
                UseDownload = AutomaticDownloadAvailable && UpgradeAllowed;
                await RefreshSourcesAsync(false);
                loaded = true;
                UpdateImageInfo();
                SetStatus(() => T["UP_Ready"]);
            }
            finally
            {
                loading = false;
                OnPropertyChanged(string.Empty);
            }
        }

        private async Task RefreshSourcesAsync(bool announce)
        {
            var found = await Task.Run(() => new
            {
                Isos = engine.FindLocalIsos(),
                Mounted = engine.FindMountedSources(),
                Drives = engine.FindDownloadLocations()
            });
            var typed = sourcePath;
            Sources.Clear();
            foreach (var path in found.Isos.Concat(found.Mounted.Select(source => source.Root)).Distinct(StringComparer.OrdinalIgnoreCase))
                Sources.Add(path);
            SourcePath = typed;
            OnPropertyChanged(nameof(HasFoundSources));
            OnPropertyChanged(nameof(SelectedFoundSource));

            var keep = selectedDestination?.Folder;
            Destinations.Clear();
            foreach (var drive in found.Drives)
                Destinations.Add(new DownloadDestination(Path.Combine(drive.RootPath, "PcNinja Downloads"), drive.ToString()));
            if (chosenFolder != null) Destinations.Add(chosenFolder);
            SelectedDestination = Destinations.FirstOrDefault(d => string.Equals(d.Folder, keep, StringComparison.OrdinalIgnoreCase))
                ?? Destinations.FirstOrDefault(d => d != chosenFolder) ?? chosenFolder;
            if (announce) SetStatus(() => T["UP_Ready"]);
        }

        // ---------------------------------------------------------------- validate and upgrade

        private void StartOrCancel()
        {
            if (IsBusy)
            {
                if (setupStarted || operation == null) return;
                IsCancelling = true;
                operation.Cancel();
                return;
            }

            Run(() => PrepareOrUpgradeAsync(true));
        }

        private async Task PrepareOrUpgradeAsync(bool startSetup)
        {
            ClearNotice();
            using (var cts = new CancellationTokenSource())
            {
                operation = cts;
                setupStarted = false;
                IsBusy = true;
                try
                {
                    await PrepareOrUpgradeCoreAsync(startSetup, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    engine.InvalidateIsoValidation();
                    SetStatus(() => T["UP_Status_Cancelled"]);
                }
                catch (Exception ex)
                {
                    engine.InvalidateIsoValidation();
                    ShowIssue(ex);
                }
                finally
                {
                    operation = null;
                    setupStarted = false;
                    IsCancelling = false;
                    IsBusy = false;
                    ProgressIndeterminate = true;
                    StatusDetail = null;
                    var keep = status;
                    try { await RefreshSourcesAsync(false); } catch (Exception ex) { UpgradeLog.Error("Refreshing sources failed: " + ex); }
                    SetStatus(keep);
                    OnPropertyChanged(nameof(StartLabel));
                }
            }
        }

        private async Task PrepareOrUpgradeCoreAsync(bool startSetup, CancellationToken token)
        {
            if (startSetup) ManualUpgradePolicy.EnsureAvailable(system);
            var download = UseDownload;
            if (download) AutomaticDownloadPolicy.EnsureAvailable(DownloadSystem.Read());
            if (startSetup)
            {
                var storage = await Task.Run(() => engine.CheckSystemDriveSpace());
                if (!storage.IsValid) throw new InvalidOperationException(storage.Message);
            }

            var previousSource = preparedSource;
            var previousKey = preparedKey;
            var previousIndex = SelectedImage?.Index;
            var consentedRestart = AutoRestart;
            var consentedQuiet = Quiet;
            MountedSource automaticSource = null;
            string path;

            if (download)
            {
                if (SelectedDestination == null) throw new InvalidOperationException(T["UP_NoDestination"]);
                var destination = SelectedDestination.Folder;
                if (!Dialogs.Confirm(T.Format("UP_Confirm_Download", destination, RestartLine(consentedRestart), consentedQuiet ? T["UP_Confirm_Silent"] : string.Empty)))
                {
                    SetStatus(() => T["UP_Status_Cancelled"]);
                    return;
                }

                var progress = new Progress<DownloadProgress>(value =>
                {
                    if (token.IsCancellationRequested) return;
                    ProgressIndeterminate = !value.Percentage.HasValue;
                    if (value.Percentage.HasValue) ProgressValue = Math.Max(0, Math.Min(100, value.Percentage.Value));
                    var amount = value.Percentage.HasValue
                        ? value.Percentage.Value.ToString(CultureInfo.CurrentCulture) + "%"
                        : UpgradeEngine.FormatBytes(value.BytesReceived);
                    SetStatus(() => T.Format("UP_Status_Downloading", Path.GetFileName(value.DestinationPath), amount));
                    StatusDetail = value.Stage;
                });
                try
                {
                    var result = await engine.DownloadIsoAsync(destination, progress, token);
                    automaticSource = result.ValidatedSource
                        ?? throw new InvalidOperationException("The downloaded ISO did not complete media validation.");
                    useDownload = false;
                    OnPropertyChanged(nameof(UseDownload));
                    OnPropertyChanged(nameof(UseExisting));
                    sourcePath = result.IsoPath;
                    OnPropertyChanged(nameof(SourcePath));
                    OnPropertyChanged(nameof(HasSourcePath));
                }
                catch (DownloadTransferException ex)
                {
                    UpgradeLog.Error("Automatic download failed: " + ex);
                    OpenPage(DownloadSourcePolicy.GetPcNinjaBrowserPage(DownloadSystem.Read()));
                    useDownload = false;
                    OnPropertyChanged(nameof(UseDownload));
                    OnPropertyChanged(nameof(UseExisting));
                    SetNotice(() => T["UP_DownloadFailed"], ex.Message, true);
                    SetStatus(() => T["UP_DownloadFailed"]);
                    return;
                }
            }

            path = CleanPath(SourcePath);
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException(T["UP_NoSource"]);

            ProgressIndeterminate = true;
            SetStatus(() => T["UP_Status_Validating"]);
            var stages = new Progress<string>(stage =>
            {
                if (!token.IsCancellationRequested) StatusDetail = stage;
            });
            ClearPrepared();

            MountedSource source;
            if (automaticSource != null) source = automaticSource;
            else if (path.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
                source = (await engine.PrepareIsoAsync(path, UpgradeEngine.ConfiguredIsoSha256, token, stages)).Source;
            else source = await engine.ValidateMountedSourceAsync(path, token, progress: stages);

            var images = source.Images ?? new InstallationImage[0];
            var sameSource = previousSource != null && previousKey == path && source.ImagePath == previousSource.ImagePath &&
                images.SequenceEqual(previousSource.Images ?? new InstallationImage[0]);
            preparedSource = source;
            preparedKey = path;
            foreach (var image in images) Images.Add(image);
            OnPropertyChanged(nameof(HasImages));
            OnPropertyChanged(nameof(ShowImagePrompt));

            var current = Current();
            IReadOnlyList<InstallationImage> compatible = IsServer
                ? images.Where(item => ManualServerUpgrade.GetIncompatibility(item, system) == null).ToList()
                : InstallationImages.Compatible(source, current);
            var index = IsServer ? (sameSource ? previousIndex : null)
                : sameSource && automaticSource == null ? previousIndex
                : compatible.Count == 1 ? compatible[0].Index : (int?)null;
            SelectedImage = images.FirstOrDefault(image => image.Index == index);
            UpdateImageInfo();
            StatusDetail = null;

            if (IsServer)
            {
                SetStatus(() => T["UP_Status_ServerInspected"]);
                SetNotice(() => T["UP_Status_ServerInspected"], ManualServerUpgrade.Advisory, true);
                return;
            }

            if (!startSetup)
            {
                PresentInspection(compatible, current);
                return;
            }

            if (SelectedImage == null)
            {
                if (compatible.Count == 0)
                {
                    SetStatus(() => T.Format("UP_Status_NoImage", current.Edition, current.Language));
                    SetNotice(status, ImageInfo, true);
                }
                else SetStatus(() => T["UP_Status_ChooseImage"]);
                return;
            }

            var selection = InstallationImages.Select(source, SelectedImage.Index, current);
            if (!ConfirmPreparedUpgrade(source, selection, compatible.Count, automaticSource, consentedRestart, consentedQuiet))
            {
                SetStatus(() => T["UP_Status_Cancelled"]);
                return;
            }

            token.ThrowIfCancellationRequested();
            SetStatus(() => T["UP_Status_Starting"]);
            var restart = AutoRestart;
            var setup = await engine.StartUpgradeAsync(source, selection.Index, restart, token, () =>
            {
                setupStarted = true;
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    OnPropertyChanged(nameof(StartLabel));
                    CommandManager.InvalidateRequerySuggested();
                }));
            }, quiet: Quiet);
            PresentSetupResult(setup, restart);
        }

        private void PresentInspection(IReadOnlyList<InstallationImage> compatible, CurrentWindows current)
        {
            if (!UpgradeAllowed)
            {
                SetStatus(() => T.Format("UP_Status_Validated", Images.Count));
                SetNotice(() => T["UP_Next_Blocked"], HostBlock, true);
                return;
            }

            if (compatible.Count == 0 || (SelectedImage != null && InstallationImages.GetIncompatibility(SelectedImage, current) != null))
            {
                SetStatus(() => T.Format("UP_Status_NoImage", current.Edition, current.Language));
                SetNotice(status, ImageInfo, true);
                return;
            }

            var chosen = SelectedImage?.Index;
            SetStatus(() => chosen.HasValue ? T.Format("UP_Status_ImageSelected", chosen.Value) : T["UP_Status_ChooseImage"]);
        }

        private bool ConfirmPreparedUpgrade(MountedSource source, InstallationImage selection, int compatibleCount,
            MountedSource automaticSource, bool consentedRestart, bool consentedQuiet)
        {
            // The automatic flow's earlier consent covers only its own unambiguous Windows 11 result
            // with unchanged options; anything else is confirmed again (as in the original tool).
            if (ReferenceEquals(source, automaticSource) && compatibleCount == 1 &&
                InstallationImages.GetSetupProfile(selection) == WindowsSetupProfile.Windows11 &&
                (source.Images ?? new InstallationImage[0]).Contains(selection) &&
                AutoRestart == consentedRestart && Quiet == consentedQuiet) return true;
            var windows10 = InstallationImages.GetSetupProfile(selection) == WindowsSetupProfile.Windows10
                ? "\n\n" + InstallationImages.Windows10Notice : string.Empty;
            return Dialogs.Confirm(T.Format("UP_Confirm_Start", selection.Name, selection.Index, RestartLine(AutoRestart),
                Quiet ? T["UP_Confirm_Silent"] : string.Empty, windows10));
        }

        private void PresentSetupResult(UpgradeResult result, bool automaticRestart)
        {
            if (result.NeedsManualRestartNotice(automaticRestart))
            {
                SetStatus(() => T["UP_Status_ManualRestart"]);
                SetNotice(() => T["UP_Status_ManualRestart"], null, false);
                Dialogs.Info(T["UP_Status_ManualRestart"]);
            }
            else if (result.ExitCode == 0) SetStatus(() => T["UP_Status_StageDone"]);
            else if (result.ExitCode == SetupTransitionExitCode)
            {
                SetStatus(() => T["UP_Status_Unconfirmed"]);
                SetNotice(() => T["UP_Status_Unconfirmed"], null, true);
            }
            else
            {
                var code = "0x" + unchecked((uint)result.ExitCode).ToString("X8", CultureInfo.InvariantCulture);
                SetStatus(() => T.Format("UP_SetupFailed", code));
                SetNotice(status, null, true);
            }
        }

        // ---------------------------------------------------------------- Server and downloads

        private bool SelectedServerImageIsUsable =>
            IsServer && preparedSource != null && SelectedImage != null &&
            (preparedSource.Images ?? new InstallationImage[0]).Count(item => item.Index == SelectedImage.Index) == 1 &&
            ManualServerUpgrade.GetIncompatibility(SelectedImage, system) == null;

        private async Task CopyServerCommandAsync()
        {
            if (!SelectedServerImageIsUsable) return;
            using (var cts = new CancellationTokenSource())
            {
                operation = cts;
                IsBusy = true;
                SetStatus(() => T["UP_Status_Validating"]);
                try
                {
                    var command = await engine.GetManualServerCommandAsync(preparedSource, SelectedImage.Index, cts.Token);
                    Clipboard.SetText(command);
                    SetStatus(() => T["UP_Status_CommandCopied"]);
                    SetNotice(() => T["UP_Status_CommandCopied"], command + "\n\n" + ManualServerUpgrade.Advisory, true);
                }
                catch (OperationCanceledException) { SetStatus(() => T["UP_Status_Cancelled"]); }
                catch (Exception ex) { ShowIssue(ex); }
                finally
                {
                    operation = null;
                    IsBusy = false;
                }
            }
        }

        private async Task FindMatchingDownloadAsync()
        {
            using (var cts = new CancellationTokenSource())
            {
                operation = cts;
                IsBusy = true;
                ProgressIndeterminate = true;
                SetStatus(() => T["UP_Status_Finding"]);
                try
                {
                    var recommendation = await engine.GetDownloadRecommendationAsync(cts.Token);
                    // The page ends the automatic attempt; it carries no source or Setup consent.
                    UseDownload = false;
                    SourcePath = string.Empty;
                    ClearPrepared();
                    OpenPage(recommendation.PageUrl);
                    var headline = recommendation.ExactMatch ? "UP_Status_PageOpened" : "UP_Status_PageOpenedNoMatch";
                    SetStatus(() => T[headline]);
                    SetNotice(status, recommendation.Message + (recommendation.FileName == null ? string.Empty : "\n" + recommendation.FileName), false);
                }
                catch (OperationCanceledException) { SetStatus(() => T["UP_Status_Cancelled"]); }
                catch (Exception ex) { ShowIssue(ex); }
                finally
                {
                    operation = null;
                    IsBusy = false;
                }
            }
        }

        // ---------------------------------------------------------------- pickers

        private void BrowseIso()
        {
            var file = FilePickers.OpenFile(T["UP_ChooseIsoTitle"], T["UP_IsoFilter"] + " (*.iso)|*.iso");
            if (file != null) SourcePath = file;
        }

        private void BrowseFolder()
        {
            var folder = FilePickers.PickFolder(T["UP_ChooseFolderTitle"]);
            if (folder != null) SourcePath = folder;
        }

        private void ChooseDownloadFolder()
        {
            var folder = FilePickers.PickFolder(T["UP_ChooseDownloadFolderTitle"]);
            if (folder == null) return;
            if (chosenFolder != null) Destinations.Remove(chosenFolder);
            chosenFolder = new DownloadDestination(folder, folder + "  " + T["UP_SelectedFolder"]);
            Destinations.Add(chosenFolder);
            SelectedDestination = chosenFolder;
            UseDownload = true;
        }

        // ---------------------------------------------------------------- helpers

        private CurrentWindows Current() => new CurrentWindows(system.Edition, system.Language, system.Build);

        private string RestartLine(bool restart) => restart ? T["UP_Confirm_Restart"] : T["UP_Confirm_NoRestart"];

        private static string CleanPath(string path) => (path ?? string.Empty).Trim().Trim('"');

        private void ClearPrepared()
        {
            preparedSource = null;
            preparedKey = null;
            Images.Clear();
            selectedImage = null;
            OnPropertyChanged(nameof(SelectedImage));
            OnPropertyChanged(nameof(HasImages));
            OnPropertyChanged(nameof(ShowImagePrompt));
            OnPropertyChanged(nameof(NextActionText));
            UpdateImageInfo();
        }

        private void UpdateImageInfo()
        {
            if (system == null)
            {
                ImageInfo = null;
                return;
            }

            if (IsServer)
            {
                var reason = SelectedImage == null ? null : ManualServerUpgrade.GetIncompatibility(SelectedImage, system);
                ImageInfo = reason;
                ImageInfoIsWarning = reason != null;
                return;
            }

            var current = Current();
            var lines = new List<string>();
            var warning = false;
            if (SelectedImage != null)
            {
                var incompatibility = InstallationImages.GetIncompatibility(SelectedImage, current);
                if (incompatibility != null)
                {
                    lines.Add(incompatibility);
                    warning = true;
                }
                else if (InstallationImages.GetSetupProfile(SelectedImage) == WindowsSetupProfile.Windows10)
                {
                    lines.Add(InstallationImages.Windows10Notice);
                    warning = true;
                }
            }
            else if (preparedSource != null && InstallationImages.Compatible(preparedSource, current).Count == 0)
            {
                lines.AddRange((preparedSource.Images ?? new InstallationImage[0])
                    .Select(image => InstallationImages.GetIncompatibility(image, current))
                    .Where(reason => reason != null).Distinct(StringComparer.Ordinal));
                warning = true;
            }

            ImageInfo = lines.Count == 0 ? null : string.Join(Environment.NewLine, lines);
            ImageInfoIsWarning = warning;
        }

        private void ShowIssue(Exception error)
        {
            UpgradeLog.Error(error.ToString());
            if (error is OperationBusyException)
            {
                SetStatus(() => T["UP_Status_Busy"]);
                SetNotice(() => T["UP_Status_Busy"], null, true);
                return;
            }

            var headline = error is UnsafeInstallationMediaException ? "UP_UnsafeMedia" : "UP_Failed";
            SetStatus(() => T[headline]);
            SetNotice(status, error.Message, true);
        }

        private void SetStatus(Func<string> text)
        {
            status = text;
            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(NextActionText));
        }

        private void SetNotice(Func<string> text, string detail, bool warning)
        {
            notice = text;
            OnPropertyChanged(nameof(Notice));
            OnPropertyChanged(nameof(HasNotice));
            NoticeDetail = detail;
            NoticeIsWarning = warning;
        }

        private void ClearNotice() => SetNotice(null, null, false);

        private void OpenPage(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                UpgradeLog.Error("Opening " + target + " failed: " + ex);
                SetNotice(() => T["UP_Failed"], ex.Message, true);
            }
        }

        /// <summary>Starts async work from a command; failures land in the notice, never in an unhandled exception.</summary>
        private async void Run(Func<Task> work)
        {
            try
            {
                await work();
            }
            catch (Exception ex)
            {
                IsBusy = false;
                ShowIssue(ex);
            }
            finally
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
}
