using System;
using System.Diagnostics;
using System.Threading;
using WinTool.Core.Diagnostics;
using WinTool.Core.Operations;
using WinTool.WindowsUpdate.Platform;
using WinTool.WindowsUpdate.Schedule;

namespace WinTool.App.Infrastructure
{
    internal static class TaskMaintenance
    {
        private static void Warn(Exception ex) => AppLog.Current.Warn("Schedule", "Task maintenance deferred or incomplete: " + ex.GetType().Name);
        internal static int RefreshForInstaller() => MaintenancePolicy.Execute(() => Service().RefreshExisting(), false, Warn);
        internal static int RemoveForInstaller() => MaintenancePolicy.Execute(() => Service().RemoveAllWaiting(TimeSpan.FromMinutes(15)), true, Warn);
        internal static ScheduleService Service()
        {
            var path = Process.GetCurrentProcess().MainModule.FileName;
            var environment = new WindowsUpdateEnvironment(path);
            return new ScheduleService(environment.Tasks, environment.Log, path, environment.Clock);
        }

        internal static IDisposable WaitForOperation(TimeSpan timeout)
        {
            var timer = Stopwatch.StartNew();
            while (true)
            {
                try { return OperationGate.EnterWindows(); }
                catch (OperationBusyException) when (timer.Elapsed < timeout) { Thread.Sleep(100); }
            }
        }

        internal static void OnElevatedStart()
        {
            if (!Program.IsAdministrator()) return;
            // Keep startup responsive while an operation runs. No persistent service:
            // a closed process or timed-out refresh is retried on the next launch.
            System.Threading.Tasks.Task.Run(() =>
            {
                try { using (WaitForOperation(TimeSpan.FromMinutes(15))) Service().RefreshExisting(); }
                catch (Exception ex) when (!(ex is OutOfMemoryException))
                { AppLog.Current.Warn("Schedule", "Worker refresh deferred: " + ex.GetType().Name); }
            });
        }

        internal static void OnManualCliStart()
        {
            if (!Program.IsAdministrator()) return;
            try { Service().RefreshExisting(); }
            catch (Exception ex) when (!(ex is OutOfMemoryException))
            { AppLog.Current.Warn("Schedule", "Worker refresh deferred: " + ex.GetType().Name); }
        }
    }
}
