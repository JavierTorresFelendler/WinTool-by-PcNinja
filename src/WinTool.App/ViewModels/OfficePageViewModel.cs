using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using WinTool.App.Infrastructure;
using WinTool.Core.Diagnostics;
using WinTool.Core.Machine;
using WinTool.Core.Operations;
using WinTool.Office;

namespace WinTool.App.ViewModels
{
    public enum OfficeMode
    {
        Install,
        Package,
        Remove
    }

    public sealed class OfficeAppOption : ObservableObject
    {
        private readonly Action changed;
        private bool isSelected;

        public OfficeAppOption(OfficeApp app, Action changed)
        {
            Id = app.Id;
            isSelected = app.IncludedByDefault;
            this.changed = changed;
        }

        public string Id { get; }

        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (Set(ref isSelected, value)) changed();
            }
        }
    }

    public sealed class OfficeChannelOption : LocalizedObject
    {
        public OfficeChannelOption(OfficeChannel channel)
        {
            Id = channel.Id;
        }

        public string Id { get; }
        public string Name => T["OF_Ch_" + Id];
        public override string ToString() => Name;
    }

    /// <summary>
    /// The Office screen (approved mockup): Smart Office Installer's choices on
    /// one page, on the same library as "office" on the command line (OFFICE-BEHAVIOR).
    /// </summary>
    public sealed class OfficePageViewModel : PageViewModel
    {
        private OfficeFamily family = OfficeFamily.Microsoft365;
        private OfficeMode mode = OfficeMode.Install;
        private OfficeChannelOption channel;
        private bool use32Bit;
        private bool visio, project, autoActivate = true, removeExisting;
        private string packageFolder = string.Empty;
        private string languageSearch = string.Empty;
        private bool addingLanguage;
        private OfficeInfo installed;
        private bool installedRead;
        private bool isBusy;
        private bool canCancel;
        private CancellationTokenSource operation;
        private OfficeProgress lastProgress;
        private Func<string> notice;
        private string noticeDetail;
        private bool noticeIsWarning;
        private string resultFolder;
        private readonly StringBuilder log = new StringBuilder();

        public OfficePageViewModel()
            : base("Nav_Office", "Page_Office_Subtitle")
        {
            Apps = new ObservableCollection<OfficeAppOption>(OfficeCatalog.Apps.Select(app => new OfficeAppOption(app, () => OnPropertyChanged(nameof(AppsSummary)))));
            Channels = OfficeCatalog.Channels.Select(c => new OfficeChannelOption(c)).ToList();
            channel = Channels.First(c => c.Id == OfficeCatalog.DefaultChannel);
            foreach (var id in OfficeCatalog.DefaultLanguages) Languages.Add(OfficeCatalog.FindLanguage(id));

            RunCommand = new RelayCommand(Start, () => !IsBusy);
            CancelCommand = new RelayCommand(() => { operation?.Cancel(); CanCancel = false; }, () => IsBusy && CanCancel);
            ToggleAddLanguageCommand = new RelayCommand(() => AddingLanguage = !AddingLanguage, () => !IsBusy);
            AddLanguageCommand = new RelayCommand<OfficeLanguage>(AddLanguage);
            RemoveLanguageCommand = new RelayCommand<OfficeLanguage>(language => { Languages.Remove(language); LanguagesChanged(); }, _ => !IsBusy);
            ChooseFolderCommand = new RelayCommand(ChooseFolder, () => !IsBusy);
            OpenResultCommand = new RelayCommand(() => Open(resultFolder), () => resultFolder != null);
        }

        public ICommand RunCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ToggleAddLanguageCommand { get; }
        public ICommand AddLanguageCommand { get; }
        public ICommand RemoveLanguageCommand { get; }
        public ICommand ChooseFolderCommand { get; }
        public ICommand OpenResultCommand { get; }

        public ObservableCollection<OfficeAppOption> Apps { get; }
        public ObservableCollection<OfficeLanguage> Languages { get; } = new ObservableCollection<OfficeLanguage>();
        public IReadOnlyList<OfficeChannelOption> Channels { get; }

        // ---------------------------------------------------------------- family and mode

        public bool IsMicrosoft365
        {
            get => family == OfficeFamily.Microsoft365;
            set { if (value) SetFamily(OfficeFamily.Microsoft365); }
        }

        public bool IsLtsc
        {
            get => family == OfficeFamily.Ltsc2024;
            set { if (value) SetFamily(OfficeFamily.Ltsc2024); }
        }

        private void SetFamily(OfficeFamily value)
        {
            if (family == value) return;
            family = value;
            OnPropertyChanged(nameof(IsMicrosoft365));
            OnPropertyChanged(nameof(IsLtsc));
            OnPropertyChanged(nameof(FamilyName));
            OnPropertyChanged(nameof(ChannelText));
        }

        public string FamilyName => IsLtsc ? "Office LTSC Professional Plus 2024" : "Microsoft 365 Apps for Enterprise";

        public bool IsInstallMode { get => mode == OfficeMode.Install; set { if (value) SetMode(OfficeMode.Install); } }
        public bool IsPackageMode { get => mode == OfficeMode.Package; set { if (value) SetMode(OfficeMode.Package); } }
        public bool IsRemoveMode { get => mode == OfficeMode.Remove; set { if (value) SetMode(OfficeMode.Remove); } }
        public bool HasChoices => mode != OfficeMode.Remove;

        private void SetMode(OfficeMode value)
        {
            if (mode == value) return;
            mode = value;
            OnPropertyChanged(nameof(IsInstallMode));
            OnPropertyChanged(nameof(IsPackageMode));
            OnPropertyChanged(nameof(IsRemoveMode));
            OnPropertyChanged(nameof(HasChoices));
            OnPropertyChanged(nameof(RunLabel));
            OnPropertyChanged(nameof(WillReplace));
        }

        public string RunLabel => mode == OfficeMode.Remove ? T["OF_Remove"] : mode == OfficeMode.Package ? T["OF_CreatePackage"] : T["OF_Install"];

        // ---------------------------------------------------------------- choices

        public OfficeChannelOption Channel
        {
            get => channel;
            set { if (value != null && Set(ref channel, value)) OnPropertyChanged(nameof(ChannelText)); }
        }

        public string ChannelText => IsLtsc ? T["OF_ChannelLtsc"] : channel.Name;

        public IReadOnlyList<string> Architectures => new[] { T["OF_Arch64"], T["OF_Arch32"] };

        public int ArchitectureIndex
        {
            get => use32Bit ? 1 : 0;
            set => use32Bit = value == 1;
        }

        public bool Visio { get => visio; set => Set(ref visio, value); }
        public bool Project { get => project; set => Set(ref project, value); }
        public bool AutoActivate { get => autoActivate; set => Set(ref autoActivate, value); }

        public bool RemoveExisting
        {
            get => removeExisting;
            set { if (Set(ref removeExisting, value)) OnPropertyChanged(nameof(WillReplace)); }
        }

        public string PackageFolder { get => packageFolder; set => Set(ref packageFolder, value ?? string.Empty); }

        public string AppsSummary => T.Format("OF_AppsCount", Apps.Count(a => a.IsSelected), Apps.Count);
        public string LanguagesSummary => string.Join(", ", Languages.Select(l => l.Id));

        // ---------------------------------------------------------------- languages

        public bool AddingLanguage
        {
            get => addingLanguage;
            set
            {
                if (!Set(ref addingLanguage, value)) return;
                LanguageSearch = string.Empty;
                OnPropertyChanged(nameof(AvailableLanguages));
            }
        }

        public string LanguageSearch
        {
            get => languageSearch;
            set { if (Set(ref languageSearch, value ?? string.Empty)) OnPropertyChanged(nameof(AvailableLanguages)); }
        }

        public IReadOnlyList<OfficeLanguage> AvailableLanguages =>
            OfficeCatalog.Languages
                .Where(l => !Languages.Contains(l))
                .Where(l => languageSearch.Length == 0 ||
                            l.Name.IndexOf(languageSearch, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            l.Id.IndexOf(languageSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList();

        private void AddLanguage(OfficeLanguage language)
        {
            if (language == null || Languages.Contains(language)) return;
            Languages.Add(language);
            LanguagesChanged();
            AddingLanguage = false;
        }

        private void LanguagesChanged()
        {
            OnPropertyChanged(nameof(LanguagesSummary));
            OnPropertyChanged(nameof(AvailableLanguages));
        }

        // ---------------------------------------------------------------- installed Office

        public string InstalledText => !installedRead ? T["OF_Reading"] : installed == null ? T["OF_NoneInstalled"] : installed.ProductName;
        public string InstalledDetails => installed == null ? string.Empty
            : string.Join(" · ", new[] { installed.Version, installed.Platform, installed.Channel }.Where(s => !string.IsNullOrEmpty(s)));
        public bool WillReplace => installed != null && (mode == OfficeMode.Remove || (mode == OfficeMode.Install && removeExisting));

        public override void OnActivated()
        {
            if (!installedRead && !IsBusy) _ = ReadInstalledAsync();
        }

        private async Task ReadInstalledAsync()
        {
            installed = await Task.Run(() =>
            {
                try { return OfficeInfo.ReadCurrent(); }
                catch (Exception ex) when (!(ex is OutOfMemoryException)) { return null; }
            });
            installedRead = true;
            OnPropertyChanged(nameof(InstalledText));
            OnPropertyChanged(nameof(InstalledDetails));
            OnPropertyChanged(nameof(WillReplace));
        }

        // ---------------------------------------------------------------- running

        public bool IsBusy
        {
            get => isBusy;
            private set
            {
                if (!Set(ref isBusy, value)) return;
                OnPropertyChanged(nameof(IsIdle));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsIdle => !isBusy;
        public bool CanCancel { get => canCancel; private set { Set(ref canCancel, value); CommandManager.InvalidateRequerySuggested(); } }
        public int ProgressValue => lastProgress?.Percent ?? 0;
        public string ProgressText => lastProgress == null ? string.Empty : StageText(lastProgress);
        public string LogText => log.ToString();
        public bool HasLog => log.Length > 0;

        public string Notice => notice?.Invoke() ?? string.Empty;
        public bool HasNotice => notice != null;
        public string NoticeDetail { get => noticeDetail; private set { Set(ref noticeDetail, value); OnPropertyChanged(nameof(HasNoticeDetail)); } }
        public bool HasNoticeDetail => !string.IsNullOrEmpty(noticeDetail);
        public bool NoticeIsWarning { get => noticeIsWarning; private set => Set(ref noticeIsWarning, value); }
        public bool HasResultFolder => resultFolder != null;

        private string StageText(OfficeProgress p)
        {
            switch (p.Stage)
            {
                case OfficeStage.FindingTool: return T["OF_Stage_FindingTool"];
                case OfficeStage.DownloadingTool: return T["OF_Stage_DownloadingTool"];
                case OfficeStage.CheckingTool: return T["OF_Stage_CheckingTool"];
                case OfficeStage.ExtractingTool: return T["OF_Stage_ExtractingTool"];
                case OfficeStage.Installing: return T["OF_Stage_Installing"];
                case OfficeStage.DownloadingOffice: return T.Format("OF_Stage_DownloadingOffice", ((p.DownloadedBytes ?? 0) / 1048576).ToString("N0", CultureInfo.CurrentCulture));
                case OfficeStage.Removing: return T["OF_Stage_Removing"];
                case OfficeStage.WritingPackage: return T["OF_Stage_WritingPackage"];
                case OfficeStage.CleaningUp: return T["OF_Stage_CleaningUp"];
                default: return string.Empty;
            }
        }

        private void Start()
        {
            var request = BuildRequest();
            if (request == null) return;
            if (mode == OfficeMode.Remove && !Dialogs.Confirm(T["OF_Confirm_Remove"])) return;
            // Confirm whatever detection found: a failed or incomplete read must not skip the question (Codex review F2).
            if (mode == OfficeMode.Install && removeExisting && !Dialogs.Confirm(T["OF_Confirm_RemoveExisting"])) return;
            _ = RunAsync(request);
        }

        private OfficeRequest BuildRequest()
        {
            var request = new OfficeRequest
            {
                Family = family,
                Channel = channel.Id,
                Apps = Apps.Where(a => a.IsSelected).Select(a => a.Id).ToList(),
                Languages = Languages.Select(l => l.Id).ToList(),
                Visio = visio,
                Project = project,
                Use32Bit = use32Bit,
                AutoActivate = autoActivate,
                RemoveExisting = mode == OfficeMode.Install && removeExisting
            };
            if (mode == OfficeMode.Remove) return request;
            string problem = null;
            if (request.Apps.Count == 0) problem = "OF_NeedApp";
            else if (request.Languages.Count == 0) problem = "OF_NeedLanguage";
            else if (mode == OfficeMode.Package && string.IsNullOrWhiteSpace(packageFolder)) problem = "OF_NeedFolder";
            if (problem == null) return request;
            SetNotice(() => T[problem], null, true);
            return null;
        }

        private async Task RunAsync(OfficeRequest request)
        {
            SetNotice(null, null, false);
            resultFolder = null;
            OnPropertyChanged(nameof(HasResultFolder));
            log.Clear();
            lastProgress = null;
            OnPropertyChanged(nameof(LogText));
            OnPropertyChanged(nameof(HasLog));
            var runMode = mode;
            using (var cts = new CancellationTokenSource())
            {
                operation = cts;
                IsBusy = true;
                CanCancel = true;
                var progress = new Progress<OfficeProgress>(OnProgress);
                try
                {
                    var deployment = new OfficeDeployment();
                    OfficeOutcome outcome;
                    switch (runMode)
                    {
                        case OfficeMode.Remove:
                            outcome = await deployment.RemoveAllAsync(false, progress, cts.Token);
                            SetNotice(() => T["OF_Done_Remove"], null, false);
                            break;
                        case OfficeMode.Package:
                            outcome = await deployment.PackageAsync(request, packageFolder, progress, cts.Token);
                            resultFolder = outcome.PackageFolder;
                            SetNotice(() => T["OF_Done_Package"], outcome.PackageFolder, false);
                            break;
                        default:
                            outcome = await deployment.InstallAsync(request, progress, cts.Token);
                            SetNotice(() => T["OF_Done_Install"], null, false);
                            break;
                    }
                }
                catch (OperationCanceledException)
                {
                    SetNotice(() => T["OF_Cancelled"], null, true);
                }
                catch (OperationBusyException)
                {
                    SetNotice(() => T["OF_Busy"], null, true);
                }
                catch (OfficeDeploymentException ex)
                {
                    var code = ex.SetupExitCode.HasValue ? ex.SetupExitCode.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
                    switch (ex.Failure)
                    {
                        case OfficeFailure.ToolNotTrusted: SetNotice(() => T["OF_ToolNotTrusted"], ex.Message, true); break;
                        case OfficeFailure.SetupFailed: SetNotice(() => T.Format("OF_SetupFailed", code), ex.Message, true); break;
                        default: SetNotice(() => T["OF_ToolUnavailable"], ex.Message, true); break;
                    }
                }
                catch (Exception ex) when (!(ex is OutOfMemoryException))
                {
                    AppLog.Current.Error("Office", "Office screen operation failed.", ex);
                    SetNotice(() => T["OF_Failed"], ex.Message, true);
                }
                finally
                {
                    operation = null;
                    CanCancel = false;
                    IsBusy = false;
                    OnPropertyChanged(nameof(HasResultFolder));
                    installedRead = false;
                    OnActivated();
                }
            }
        }

        private void OnProgress(OfficeProgress value)
        {
            lastProgress = value;
            // The Office installer is never interrupted; downloads (tool and package) can be cancelled.
            if (value.Stage == OfficeStage.Installing || value.Stage == OfficeStage.Removing || value.Stage == OfficeStage.WritingPackage)
                CanCancel = false;
            log.Append(DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)).Append("  ").AppendLine(value.Message);
            OnPropertyChanged(nameof(ProgressValue));
            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(LogText));
            OnPropertyChanged(nameof(HasLog));
        }

        private void SetNotice(Func<string> text, string detail, bool warning)
        {
            notice = text;
            OnPropertyChanged(nameof(Notice));
            OnPropertyChanged(nameof(HasNotice));
            NoticeDetail = detail;
            NoticeIsWarning = warning;
        }

        private void ChooseFolder()
        {
            var folder = FilePickers.PickFolder(T["OF_ChoosePackageFolderTitle"]);
            if (folder != null) PackageFolder = folder;
        }

        private static void Open(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return;
            try { Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); }
            catch (Exception ex) when (!(ex is OutOfMemoryException)) { AppLog.Current.Warn("Office", "Opening " + folder + " failed: " + ex.Message); }
        }
    }
}
