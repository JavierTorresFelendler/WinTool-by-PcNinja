using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Windows;
using PcNinja.Shared;
using WinTool.App.Infrastructure;
using WinTool.App.Localization;
using WinTool.Core;
using WinTool.Core.Diagnostics;
using WinTool.Core.Localization;
using WinTool.Core.Platform;
using WinTool.Core.Settings;
using WinTool.Core.Updates;
using WinTool.Core.Security;

namespace WinTool.App
{
    public static class Program
    {
        internal const int ExitOk = 0;
        internal const int ExitUsage = 2;
        internal const int ExitUnsupportedOs = 3;
        internal const int ExitAlreadyRunningNotResponding = 4;
        internal const int ExitElevationCancelled = 5;
        internal const int ExitLaunchFailed = 6;

        private const int ErrorCancelled = 1223;

        [STAThread]
        public static int Main(string[] args)
        {
            // Per-monitor DPI for WPF on .NET Framework 4.8, set in code so the portable EXE needs no .config file.
            AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges", false);
            EmbeddedAssemblies.Register();
            return Run(args ?? new string[0]);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Run(string[] args)
        {
            var hasInstaller = EmbeddedInstaller.IsPresent;
            var arguments = UniversalLaunchPolicy.Classify(args, hasInstaller);
            var portableChosen = arguments == StartupArguments.PortableGui;
            AppLog.Current.Info("App", ProductInfo.Name + " " + ProductInfo.InformationalVersion + "; mode: " + (arguments == StartupArguments.CommandLine || arguments == StartupArguments.UsageError ? "CLI" : "GUI"));
            if (WinToolPaths.DataRootOverride != null)
                AppLog.Current.Info("App", "Data folders redirected to " + WinToolPaths.DataRootOverride + " (" + WinToolPaths.DataRootVariable + ").");
            if (arguments == StartupArguments.UsageError)
            {
                AppLog.Current.Warn("App", "Portable GUI flags must be used alone.");
                return ExitUsage;
            }
            if (arguments == StartupArguments.CommandLine)
            {
                return CommandLine.Run(args);
            }

            var settings = new SettingsStore(SettingsStore.DefaultPath).Load();
            Loc.Instance.SetLanguage(LanguageResolver.Resolve(settings.Language, InstallDefault("Language"), CultureInfo.CurrentUICulture.Name, Loc.PseudoAllowed));

            var os = OsInfo.ReadCurrent();
            AppLog.Current.Info("App", "Windows: " + os.FriendlyName + " (build " + os.Build + "." + os.Revision + ")");
            if (!os.IsSupported)
            {
                ShowMessage(Loc.Instance.Format("Startup_Unsupported", os.FriendlyName), MessageBoxImage.Stop);
                return ExitUnsupportedOs;
            }

            PcNinjaSingleInstance.DiagnosticSink = message => AppLog.Current.Info("SingleInstance", message);
            var options = new PcNinjaSingleInstanceOptions();

            if (hasInstaller)
            {
                var restart = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ViewModels.AppUpdateViewModel.WaitForPidVariable));
                // Wait before probing, so a self-update does not activate its exiting predecessor.
                WaitForReplacedCopy();
                PcNinjaSingleInstanceOutcome probe;
                PcNinjaSingleInstance.TryActivateExisting(ProductInfo.SingleInstanceAppId, options, out probe);
                if (probe == PcNinjaSingleInstanceOutcome.ActivatedExisting) return ExitOk;
                if (probe == PcNinjaSingleInstanceOutcome.ExistingNotResponding) return ShowNotResponding();
                // Redirect before acquiring the guard, including an already-elevated launch.
                var launchExit = ChooseUniversalLaunch(portableChosen || restart);
                if (launchExit.HasValue) return launchExit.Value;
            }

            if (!IsAdministrator() && !ElevationSkippedForDevelopment())
            {
                PcNinjaSingleInstanceOutcome probe;
                PcNinjaSingleInstance.TryActivateExisting(ProductInfo.SingleInstanceAppId, options, out probe);
                if (probe == PcNinjaSingleInstanceOutcome.ActivatedExisting) return ExitOk;
                if (probe == PcNinjaSingleInstanceOutcome.ExistingNotResponding) return ShowNotResponding();
                if (TestOverridesSet())
                {
                    // Windows does not pass this process's environment to the elevated copy, and
                    // forwarding test settings across the UAC boundary on the command line would let a
                    // standard user steer an administrator process. Stop instead of silently using real data.
                    AppLog.Current.Warn("App", "Test settings are set; not restarting with administrator rights because they would be lost.");
                    ShowMessage(Loc.Instance["Startup_TestOverridesNeedAdmin"], MessageBoxImage.Warning);
                    return ExitElevationCancelled;
                }
                return RestartElevated(EmbeddedInstaller.IsPresent ? PortableChosenArgument : null);
            }

            WaitForReplacedCopy();

            PcNinjaSingleInstanceOutcome outcome;
            using (var holder = PcNinjaSingleInstance.Acquire(ProductInfo.SingleInstanceAppId, options, out outcome))
            {
                if (outcome == PcNinjaSingleInstanceOutcome.ActivatedExisting) return ExitOk;
                if (outcome == PcNinjaSingleInstanceOutcome.ExistingNotResponding) return ShowNotResponding();
                if (holder == null && outcome != PcNinjaSingleInstanceOutcome.GuardUnavailable && outcome != PcNinjaSingleInstanceOutcome.NestedOwner)
                {
                    return ExitOk;
                }

                if (hasInstaller)
                {
                    var fresh = ReadLaunchFacts(true);
                    if (UniversalLaunchPolicy.Decide(fresh.Facts) == UniversalLaunch.BlockOlder) return ShowOlderBlocked(fresh);
                }

                try { MachineDataTransition.Initialize(deferWhenBusy: true); }
                catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
                { AppLog.Current.Error("Storage", "Machine data initialization failed.", ex); ShowMessage(Loc.Instance["Startup_DataInitializationFailed"], MessageBoxImage.Stop); return 24; }
                TaskMaintenance.OnElevatedStart();

                var app = new App();
                app.InitializeComponent();
                return app.RunMainWindow(settings);
            }
        }

        /// <summary>A portable update starts the new copy before the old one exits; wait (at most 30 s) for it.</summary>
        private static void WaitForReplacedCopy()
        {
            var text = Environment.GetEnvironmentVariable(ViewModels.AppUpdateViewModel.WaitForPidVariable);
            if (string.IsNullOrEmpty(text)) return;
            Environment.SetEnvironmentVariable(ViewModels.AppUpdateViewModel.WaitForPidVariable, null);
            int pid;
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out pid)) return;
            try
            {
                using (var previous = Process.GetProcessById(pid)) previous.WaitForExit(30000);
            }
            catch (ArgumentException)
            {
                // Already gone.
            }
        }

        /// <summary>A value the MSI wrote under HKLM\SOFTWARE\PcNinja\WinTool, or null.</summary>
        internal static string InstallDefault(string name)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\PcNinja\WinTool")) return key?.GetValue(name) as string;
            }
            catch (System.Security.SecurityException)
            {
                return null;
            }
        }

        internal static bool IsAdministrator()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        private static bool ElevationSkippedForDevelopment()
        {
#if DEBUG
            return Environment.GetEnvironmentVariable("WINTOOL_NO_ELEVATION") == "1";
#else
            return false;
#endif
        }

        private static bool TestOverridesSet() =>
            !string.IsNullOrWhiteSpace(TestEnvironment.Get(WinToolPaths.DataRootVariable)) ||
            !string.IsNullOrWhiteSpace(TestEnvironment.Get(WinTool.Core.Updates.AppUpdater.FeedVariable));

        /// <summary>Internal: the elevated restart after the user chose Run portable; not a command.</summary>
        internal const string PortableChosenArgument = UniversalLaunchPolicy.InternalPortableArgument;

        /// <summary>Null continues this GUI; a value ends startup before migration/maintenance (D-039).</summary>
        private static int? ChooseUniversalLaunch(bool portableIntent)
        {
            var observed = ReadLaunchFacts(portableIntent);
            var decision = UniversalLaunchPolicy.Decide(observed.Facts);
            AppLog.Current.Info("App", "Universal launch decision: " + decision + ".");
            if (decision == UniversalLaunch.BlockOlder) return ShowOlderBlocked(observed);

            var answer = LaunchAnswer.Cancel;
            var options = Loc.Instance.FlowDirection == FlowDirection.RightToLeft
                ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign : MessageBoxOptions.None;
            if (decision == UniversalLaunch.AskInstall || decision == UniversalLaunch.AskUpdate || decision == UniversalLaunch.AskUnprotected)
            {
                var canUpdate = UniversalLaunchPolicy.CanInstall(observed.Facts);
                var text = decision == UniversalLaunch.AskInstall ? Loc.Instance["Startup_InstallOrPortable"] :
                    decision == UniversalLaunch.AskUpdate ? Loc.Instance.Format("Startup_UpdateInstalled", observed.Version, AppVersion.Current) :
                    Loc.Instance.Format(canUpdate ? "Startup_UnprotectedUpdate" : "Startup_UnprotectedPortable", observed.Path);
                var portableOnly = decision == UniversalLaunch.AskUnprotected && !canUpdate;
                var result = MessageBox.Show(text, ProductInfo.Name,
                    portableOnly ? MessageBoxButton.OKCancel : MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question, MessageBoxResult.Cancel, options);
                answer = result == MessageBoxResult.Yes ? LaunchAnswer.Yes :
                    result == MessageBoxResult.No || (portableOnly && result == MessageBoxResult.OK) ? LaunchAnswer.No : LaunchAnswer.Cancel;
            }

            var action = UniversalLaunchPolicy.Resolve(observed.Facts, answer);
            if (action == LaunchAction.Stop) return ExitOk;
            if (action == LaunchAction.RunCurrent)
            {
                // A dialog/marker is intent only. A registry/version change can still block this copy.
                var fresh = ReadLaunchFacts(true);
                if (UniversalLaunchPolicy.Decide(fresh.Facts) == UniversalLaunch.BlockOlder) return ShowOlderBlocked(fresh);
                return null;
            }
            if (action == LaunchAction.OpenInstalled)
            {
                try
                {
                    var fresh = ReadLaunchFacts(false);
                    if (fresh.Path != observed.Path || fresh.Version?.FileVersion != observed.Version?.FileVersion ||
                        !fresh.Facts.InstalledProtected || !ProtectedMachineData.IsProtectedExecutable(fresh.Path))
                        throw new InvalidOperationException(Loc.Instance["Startup_InstalledChanged"]);
                    var child = Process.Start(new ProcessStartInfo(fresh.Path)
                    { UseShellExecute = true, WorkingDirectory = System.IO.Path.GetDirectoryName(fresh.Path) });
                    if (child == null) throw new InvalidOperationException(Loc.Instance["Startup_NoProcess"]);
                    child.Dispose();
                    AppLog.Current.Info("App", "Started protected installed copy " + fresh.Path + ".");
                    return ExitOk;
                }
                catch (Exception ex) when (ex is Win32Exception || ex is System.IO.IOException || ex is InvalidOperationException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is System.Security.SecurityException)
                {
                    AppLog.Current.Error("App", "Installed copy launch failed; no fallback.", ex);
                    ShowMessage(Loc.Instance.Format("Startup_InstalledLaunchFailed", observed.Path, ex.Message), MessageBoxImage.Error);
                    return ExitLaunchFailed;
                }
            }

            // Only explicit Install/Update consent reaches here; never downgrade after a changed lookup.
            if (!UniversalLaunchPolicy.CanInstall(ReadLaunchFacts(false).Facts))
            {
                ShowMessage(Loc.Instance["Startup_InstalledChanged"], MessageBoxImage.Warning);
                return ExitLaunchFailed;
            }
            try { EmbeddedInstaller.StartInstall(); return ExitOk; }
            catch (Exception ex) when (ex is System.IO.IOException || ex is InvalidOperationException || ex is Win32Exception || ex is UnauthorizedAccessException)
            {
                AppLog.Current.Error("App", "The embedded installer could not start.", ex);
                ShowMessage(Loc.Instance.Format("Startup_InstallFailed", ex.Message), MessageBoxImage.Error);
                return ExitLaunchFailed;
            }
        }

        private sealed class LaunchObservation
        {
            public UniversalLaunchFacts Facts;
            public string Path;
            public AppVersion Version;
        }

        private static LaunchObservation ReadLaunchFacts(bool portableIntent)
        {
            var exe = Process.GetCurrentProcess().MainModule.FileName;
            var folder = InstalledCopy.ReadInstallFolder();
            var result = new LaunchObservation { Facts = new UniversalLaunchFacts
            {
                HasInstaller = true, PortableIntent = portableIntent, CurrentVersion = AppVersion.Current.FileVersion,
                IsInstalledSelf = InstalledCopy.IsInstalled(exe, folder),
                IsProtectedWorker = ProtectedTaskCopy.IsManagedPath(exe) && ProtectedMachineData.IsProtectedExecutable(exe)
            }};
            try
            {
                if (string.IsNullOrWhiteSpace(folder)) return result;
                if (!System.IO.Path.IsPathRooted(folder)) throw new ArgumentException("Install folder is not absolute.");
                var path = System.IO.Path.Combine(folder, ReleaseFiles.Executable);
                var info = FileVersionInfo.GetVersionInfo(path);
                AppVersion version;
                if (!InstalledCopy.TryReadVersion(info.ProductVersion, info.FileVersion, out version))
                    throw new InvalidOperationException("Installed version metadata is missing or inconsistent.");
                result.Path = path;
                result.Version = version;
                result.Facts.InstalledVersion = version.FileVersion;
                result.Facts.InstalledProtected = ProtectedMachineData.IsProtectedExecutable(path);
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException || ex is InvalidOperationException || ex is System.Security.SecurityException)
            {
                AppLog.Current.Warn("App", "Could not check registered installed copy: " + ex.Message);
            }
            return result;
        }

        private static int ShowOlderBlocked(LaunchObservation observed)
        {
            AppLog.Current.Warn("App", "Older portable GUI blocked before machine initialization.");
            ShowMessage(Loc.Instance.Format("Startup_OlderPortableBlocked", AppVersion.Current, observed.Version, observed.Path), MessageBoxImage.Warning);
            return ExitLaunchFailed;
        }

        private static int RestartElevated(string argument)
        {
            try
            {
                var start = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName)
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    Arguments = argument ?? string.Empty
                };
                Process.Start(start);
                AppLog.Current.Info("App", "Restarted with administrator rights.");
                return ExitOk;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                AppLog.Current.Warn("App", "Administrator rights were declined.");
                ShowMessage(Loc.Instance["Startup_ElevationCancelled"], MessageBoxImage.Information);
                return ExitElevationCancelled;
            }
        }

        private static int ShowNotResponding()
        {
            ShowMessage(Loc.Instance["Startup_AlreadyRunning"], MessageBoxImage.Warning);
            return ExitAlreadyRunningNotResponding;
        }

        private static void ShowMessage(string text, MessageBoxImage image)
        {
            var options = Loc.Instance.FlowDirection == FlowDirection.RightToLeft
                ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign
                : MessageBoxOptions.None;
            MessageBox.Show(text, ProductInfo.Name, MessageBoxButton.OK, image, MessageBoxResult.OK, options);
        }
    }
}
