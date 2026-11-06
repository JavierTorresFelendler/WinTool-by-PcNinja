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
            AppLog.Current.Info("App", ProductInfo.Name + " " + ProductInfo.InformationalVersion + "; mode: " + (args.Length > 0 ? "CLI" : "GUI"));
            if (args.Length > 0)
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
                return RestartElevated();
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

        private static int RestartElevated()
        {
            try
            {
                var start = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName)
                {
                    UseShellExecute = true,
                    Verb = "runas"
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
