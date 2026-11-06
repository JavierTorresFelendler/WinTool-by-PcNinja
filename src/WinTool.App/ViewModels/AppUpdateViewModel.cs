using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using WinTool.Core;
using WinTool.Core.Diagnostics;
using WinTool.Core.Settings;
using WinTool.Core.Updates;

namespace WinTool.App.ViewModels
{
    /// <summary>
    /// WinTool's own update (D-018, D-023): check GitHub, show what is new, update
    /// only when the user agrees. An installed copy runs the new MSI; a portable copy
    /// replaces itself and keeps the previous EXE as WinTool-by-PcNinja-v&lt;version&gt;.exe.
    /// </summary>
    public sealed class AppUpdateViewModel : LocalizedObject, IOperationPage
    {
        /// <summary>Set for the new copy after a portable update: the PID of the copy it replaces.</summary>
        public const string WaitForPidVariable = "WINTOOL_WAIT_FOR_PID";

        /// <summary>Set for the new copy after a portable update: the version it replaced.</summary>
        public const string UpdatedFromVariable = "WINTOOL_UPDATED_FROM";

        private const int MaxNotes = 900;

        private readonly AppSettings settings;
        private readonly string exePath;
        private readonly AppUpdater updater = new AppUpdater();
        private bool busy;
        private string status;

        public AppUpdateViewModel(AppSettings settings, string exePath)
        {
            this.settings = settings;
            this.exePath = exePath;
        }

        /// <summary>True while another WinTool operation runs; an update waits for it.</summary>
        public Func<bool> OperationRunning { get; set; } = () => false;

        public bool IsBusy { get => busy; private set => Set(ref busy, value); }

        /// <summary>A WinTool download is short; closing waits for it.</summary>
        public void RequestCancel()
        {
        }
        public string Status { get => status; private set => Set(ref status, value); }

        public bool IsInstalled => exePath.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase);

        /// <summary>At startup: quiet when nothing is found or the check fails.</summary>
        public void CheckAtStartup()
        {
            var updatedFrom = Environment.GetEnvironmentVariable(UpdatedFromVariable);
            if (!string.IsNullOrEmpty(updatedFrom))
            {
                Environment.SetEnvironmentVariable(UpdatedFromVariable, null);
                Dialogs.Info(T.Format("UPD_Done", updatedFrom, AppVersion.Current));
                return;
            }

            if (!settings.CheckForAppUpdatesAtStartup || Program.InstallDefault("CheckForAppUpdates") == "0") return;
            _ = CheckAsync(false);
        }

        public async Task CheckAsync(bool manual)
        {
            if (IsBusy) return;
            IsBusy = true;
            Status = T["UPD_Checking"];
            ReleaseInfo release;
            try
            {
                release = await updater.CheckAsync(AppVersion.Current, settings.IncludeTestVersions, CancellationToken.None);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                AppLog.Current.Warn("Update", "Update check failed: " + ex.Message);
                Status = null;
                IsBusy = false;
                if (manual) Dialogs.Error(T["UPD_CheckFailed"] + "\n\n" + ex.Message);
                return;
            }

            IsBusy = false;
            Status = null;
            if (release == null)
            {
                AppLog.Current.Info("Update", "No newer version (" + AppVersion.Current + (settings.IncludeTestVersions ? ", test versions included" : string.Empty) + ").");
                if (manual) Dialogs.Info(T.Format("UPD_UpToDate", AppVersion.Current));
                return;
            }

            AppLog.Current.Info("Update", "Version " + release.Version + " is available.");
            var notes = release.Notes.Length > MaxNotes ? release.Notes.Substring(0, MaxNotes) + "…" : release.Notes;
            var key = release.Version.IsTest ? "UPD_AvailableTest" : "UPD_Available";
            if (!Dialogs.Confirm(T.Format(key, release.Version, AppVersion.Current, notes.Trim()))) return;
            await InstallAsync(release);
        }

        private async Task InstallAsync(ReleaseInfo release)
        {
            if (OperationRunning())
            {
                Dialogs.Info(T["UPD_WaitForOperation"]);
                return;
            }

            IsBusy = true;
            var asset = IsInstalled ? ReleaseFiles.Installer : ReleaseFiles.Executable;
            var progress = new Progress<long>(bytes => Status = T.Format("UPD_Downloading", (bytes / 1048576.0).ToString("N1", T.Culture)));
            try
            {
                Status = T["UPD_Checking"];
                var file = await updater.DownloadVerifiedAsync(release, asset, progress, CancellationToken.None);
                AppLog.Current.Info("Update", "Downloaded and verified " + file);
                if (IsInstalled) RunInstaller(file);
                else ReplacePortable(file, release);
            }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            {
                AppLog.Current.Error("Update", "Update failed.", ex);
                Dialogs.Error(T["UPD_Failed"] + "\n\n" + ex.Message);
            }
            finally
            {
                IsBusy = false;
                Status = null;
            }
        }

        /// <summary>The MSI replaces the installed copy; WinTool closes so its files are free.</summary>
        private static void RunInstaller(string msi)
        {
            Process.Start(new ProcessStartInfo("msiexec.exe", "/i \"" + msi + "\"") { UseShellExecute = true });
            AppLog.Current.Info("Update", "Started the installer; closing.");
            Application.Current.Shutdown();
        }

        private void ReplacePortable(string downloaded, ReleaseInfo release)
        {
            var current = AppVersion.Current;
            var archive = PortableSwap.Apply(exePath, downloaded, current.FileVersion);
            AppLog.Current.Info("Update", "Previous copy kept as " + archive);
            var start = new ProcessStartInfo(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(exePath), ReleaseFiles.Executable))
            {
                UseShellExecute = false,
                WorkingDirectory = System.IO.Path.GetDirectoryName(exePath)
            };
            start.EnvironmentVariables[WaitForPidVariable] = Process.GetCurrentProcess().Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            start.EnvironmentVariables[UpdatedFromVariable] = current.ToString();
            Process.Start(start);
            Application.Current.Shutdown();
        }
    }
}
