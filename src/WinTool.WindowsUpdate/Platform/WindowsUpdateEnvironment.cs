using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Principal;
using WinTool.Core.Diagnostics;
using WinTool.Core.Operations;
using WinTool.WindowsUpdate.Engine;
using WinTool.WindowsUpdate.Logic;
using WinTool.WindowsUpdate.Migration;
using WinTool.WindowsUpdate.Schedule;
using WinTool.WindowsUpdate.Storage;

namespace WinTool.WindowsUpdate.Platform
{
    public sealed class WindowsUpdateEnvironment : IWindowsUpdateEnvironment
    {
        private readonly WindowsSystemProbe probe;

        public WindowsUpdateEnvironment(string executablePath, bool useEventLog = true)
        {
            ExecutablePath = executablePath;
            var log = new RunLog(AppLog.Current, useEventLog);
            Log = log;
            probe = new WindowsSystemProbe(log);
            Store = new WindowsUpdateStore(WindowsUpdateStore.DefaultFolder);
            Tasks = new ComTaskScheduler();
        }

        public IRunLog Log { get; }
        public IWindowsUpdateStore Store { get; }
        public ISystemProbe Probe => probe;
        public ITaskSchedulerService Tasks { get; }
        public Func<DateTime> Clock { get; } = () => DateTime.Now;
        public string ExecutablePath { get; }

        public string WindowsVersion
        {
            get
            {
                try
                {
                    var os = WinTool.Core.Platform.OsInfo.ReadCurrent();
                    return os.FriendlyName + " (build " + os.Build + "." + os.Revision + ")";
                }
                catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException)
                {
                    return Environment.OSVersion.VersionString;
                }
            }
        }

        public string DataFolder => WinTool.Core.WinToolPaths.MachineData;

        public bool IsAdministrator
        {
            get
            {
                using (var identity = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        public IUpdateAgent CreateAgent() => new WuaUpdateAgent(Log);

        public IDisposable TryAcquireOperationLock() => OperationLock.TryAcquire("Windows Update", TimeSpan.Zero);

        public WindowsUpdateReset CreateReset() =>
            new WindowsUpdateReset(probe, new WindowsFolderOperations(), Log, Environment.GetFolderPath(Environment.SpecialFolder.Windows), Clock);

        public HealthFacts CollectHealthFacts(bool checkNetwork)
        {
            bool? registered;
            try { registered = WuaUpdateAgent.IsMicrosoftUpdateRegistered(); }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException || ex is UnauthorizedAccessException) { registered = null; }

            return new HealthFacts
            {
                ServicesCanRun = WindowsSystemProbe.ServicesCanRun(),
                SystemDriveFreeBytes = WindowsSystemProbe.SystemDriveFreeBytes(),
                PendingRestart = probe.GetPendingRestart(),
                Activity = probe.GetActivity(),
                NetworkReady = checkNetwork ? probe.CheckNetwork().Ready : (bool?)null,
                MicrosoftUpdateRegistered = registered
            };
        }

        public ILegacyInstallations LegacyInstallations { get; } = new WindowsLegacyInstallations();

        public string ReadLegacyConfig()
        {
            var path = LegacyWinUpdateConfig.DefaultPath;
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        public IEnumerable<string> LogSources()
        {
            yield return AppLog.DefaultDirectory;
            yield return Path.Combine(WinTool.Core.WinToolPaths.LegacyMachineData, "Logs");
            yield return WindowsUpdateStore.DefaultFolder;
        }
    }
}
