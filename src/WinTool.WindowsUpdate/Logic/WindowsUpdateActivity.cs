using System;
using System.Collections.Generic;
using System.Linq;

namespace WinTool.WindowsUpdate.Logic
{
    public enum ActivityStatus
    {
        Idle,
        BackgroundActivity,
        Installing
    }

    public sealed class WindowsUpdateActivity
    {
        public WindowsUpdateActivity(ActivityStatus status, IReadOnlyList<string> installProcesses, IReadOnlyList<string> backgroundProcesses)
        {
            Status = status;
            InstallProcesses = installProcesses;
            BackgroundProcesses = backgroundProcesses;
        }

        public ActivityStatus Status { get; }
        public bool IsInstalling => Status == ActivityStatus.Installing;
        public bool HasBackgroundActivity => Status == ActivityStatus.BackgroundActivity;
        public IReadOnlyList<string> InstallProcesses { get; }
        public IReadOnlyList<string> BackgroundProcesses { get; }

        public string Message
        {
            get
            {
                switch (Status)
                {
                    case ActivityStatus.Installing: return "Windows Update or Windows servicing is installing. WinTool will not interrupt it.";
                    case ActivityStatus.BackgroundActivity: return "Windows Update is scanning or downloading in the background.";
                    default: return "No active Windows Update operation was detected.";
                }
            }
        }

        public static WindowsUpdateActivity Idle { get; } = new WindowsUpdateActivity(ActivityStatus.Idle, new string[0], new string[0]);
    }

    /// <summary>WU-30. Background transfers are detected through the update processes, not BITS jobs.</summary>
    public static class ActivityEvaluator
    {
        public static readonly string[] InstallProcessNames = { "TrustedInstaller", "TiWorker", "WindowsUpdateBox", "SetupHost" };
        public static readonly string[] BackgroundProcessNames = { "MoUsoCoreWorker", "UsoClient", "wuauclt", "MusNotification", "MusNotificationUx" };

        public static WindowsUpdateActivity Evaluate(IEnumerable<string> runningProcessNames, bool trustedInstallerServiceRunning)
        {
            var running = new HashSet<string>(runningProcessNames ?? new string[0], StringComparer.OrdinalIgnoreCase);
            var install = InstallProcessNames.Where(running.Contains).ToList();
            var background = BackgroundProcessNames.Where(running.Contains).ToList();

            ActivityStatus status;
            if (install.Count > 0 || trustedInstallerServiceRunning) status = ActivityStatus.Installing;
            else if (background.Count > 0) status = ActivityStatus.BackgroundActivity;
            else status = ActivityStatus.Idle;

            return new WindowsUpdateActivity(status, install, background);
        }
    }
}
