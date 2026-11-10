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

namespace WinTool.App
{
    public static class Program
    {
        internal const int ExitOk = 0;
        internal const int ExitUsage = 2;
        internal const int ExitUnsupportedOs = 3;
        internal const int ExitAlreadyRunningNotResponding = 4;
        internal const int ExitElevationCancelled = 5;

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
            // The elevated restart of a launch whose user already answered Install / Run portable (D-026).
            var portableChosen = args.Length == 1 && args[0] == PortableChosenArgument;
            AppLog.Current.Info("App", ProductInfo.Name + " " + ProductInfo.InformationalVersion + "; mode: " + (args.Length > 0 && !portableChosen ? "CLI" : "GUI"));
            if (WinToolPaths.DataRootOverride != null)
                AppLog.Current.Info("App", "Data folders redirected to " + WinToolPaths.DataRootOverride + " (" + WinToolPaths.DataRootVariable + ").");
            if (args.Length > 0 && !portableChosen)
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

            if (!IsAdministrator() && !ElevationSkippedForDevelopment())
            {
                PcNinjaSingleInstanceOutcome probe;
                PcNinjaSingleInstance.TryActivateExisting(ProductInfo.SingleInstanceAppId, options, out probe);
                if (probe == PcNinjaSingleInstanceOutcome.ActivatedExisting) return ExitOk;
                if (probe == PcNinjaSingleInstanceOutcome.ExistingNotResponding) return ShowNotResponding();
                // Asked before elevation: installing runs the MSI, which asks for administrator rights itself.
                if (!portableChosen && !AskInstallOrPortable()) return ExitOk;
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

            // A self-update restart (WaitForPid) is not a manual launch: it never asks.
            var askHere = !portableChosen && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ViewModels.AppUpdateViewModel.WaitForPidVariable));
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

                // Started with administrator rights already: asked here, after a running window was activated instead.
                if (askHere && !AskInstallOrPortable()) return ExitOk;

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
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(WinToolPaths.DataRootVariable)) ||
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(WinTool.Core.Updates.AppUpdater.FeedVariable));

        /// <summary>Internal: the elevated restart after the user chose Run portable; not a command.</summary>
        internal const string PortableChosenArgument = "--portable-chosen";

        /// <summary>
        /// D-026 (owner, after T-011): each manual launch of a downloaded universal EXE asks Install or
        /// Run portable. Returns false when this process should end (the installer was started, or the
        /// user closed the question). Never asked for the command line or an installed copy.
        /// </summary>
        private static bool AskInstallOrPortable()
        {
            if (!EmbeddedInstaller.IsPresent) return true;
            var exe = Process.GetCurrentProcess().MainModule.FileName;
            if (WinTool.Core.Updates.InstalledCopy.IsInstalled(exe)) return true;

            // The installed copy itself (also outside Program Files) never asks.
            var installed = FindInstalledCopy();
            var choice = WinTool.Core.Updates.InstalledCopy.Decide(exe, installed?.Item1, installed?.Item2, WinTool.Core.Updates.AppVersion.Current.FileVersion);
            if (choice == WinTool.Core.Updates.LaunchChoice.RunThis) return true;

            var options = Loc.Instance.FlowDirection == FlowDirection.RightToLeft
                ? MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign
                : MessageBoxOptions.None;
            var answer = MessageBox.Show(Loc.Instance["Startup_InstallOrPortable"], ProductInfo.Name,
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.Yes, options);
            if (answer == MessageBoxResult.Cancel) return false;
            if (answer == MessageBoxResult.No)
            {
                AppLog.Current.Info("App", "Run portable chosen.");
                return true;
            }

            // Install chosen, but this version or a newer one is already installed: start it instead of
            // reinstalling (owner, 2026-11-07). An older installed version is updated by the installer.
            if (choice == WinTool.Core.Updates.LaunchChoice.RunInstalled)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(installed.Item1) { UseShellExecute = true, WorkingDirectory = System.IO.Path.GetDirectoryName(installed.Item1) });
                    AppLog.Current.Info("App", "Install chosen; WinTool " + installed.Item2 + " is already installed, started " + installed.Item1 + ".");
                    return false;
                }
                catch (Exception ex) when (ex is Win32Exception || ex is System.IO.IOException || ex is InvalidOperationException)
                {
                    AppLog.Current.Warn("App", "Could not start the installed copy " + installed.Item1 + "; running the installer: " + ex.Message);
                }
            }

            try
            {
                EmbeddedInstaller.StartInstall();
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is System.IO.InvalidDataException || ex is InvalidOperationException || ex is Win32Exception || ex is UnauthorizedAccessException)
            {
                AppLog.Current.Error("App", "The embedded installer could not start.", ex);
                ShowMessage(Loc.Instance.Format("Startup_InstallFailed", ex.Message), MessageBoxImage.Error);
            }

            return false;
        }

        /// <summary>The installed WinTool (path and file version) from the MSI's InstallFolder value, or null.</summary>
        private static Tuple<string, Version> FindInstalledCopy()
        {
            try
            {
                var folder = InstallDefault("InstallFolder");
                if (string.IsNullOrWhiteSpace(folder)) return null;
                var path = System.IO.Path.Combine(folder, ReleaseFiles.Executable);
                if (!System.IO.File.Exists(path)) return null;
                Version version;
                return Version.TryParse(FileVersionInfo.GetVersionInfo(path).FileVersion, out version) ? Tuple.Create(path, version) : null;
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                AppLog.Current.Warn("App", "Could not check for an installed copy: " + ex.Message);
                return null;
            }
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
