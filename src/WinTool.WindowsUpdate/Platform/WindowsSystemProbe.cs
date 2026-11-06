using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.ServiceProcess;
using Microsoft.Win32;
using WinTool.WindowsUpdate.Engine;
using WinTool.WindowsUpdate.Logic;

namespace WinTool.WindowsUpdate.Platform
{
    /// <summary>Reads machine facts and controls the update services.</summary>
    public sealed class WindowsSystemProbe : ISystemProbe, IServiceControl
    {
        public static readonly string[] UpdateServices = { "wuauserv", "bits", "UsoSvc", "dosvc", "cryptsvc" };
        private static readonly TimeSpan ServiceWait = TimeSpan.FromSeconds(30);

        private readonly IRunLog log;

        public WindowsSystemProbe(IRunLog log)
        {
            this.log = log;
        }

        public PendingRestartState GetPendingRestart()
        {
            var facts = new PendingRestartFacts
            {
                ComponentServicingRebootPending = KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending"),
                WindowsUpdateRebootRequired = KeyExists(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"),
                PendingFileRenameOperations = Value(@"SYSTEM\CurrentControlSet\Control\Session Manager", "PendingFileRenameOperations") as string[],
                ComputerName = Value(@"SYSTEM\CurrentControlSet\Control\ComputerName\ComputerName", "ComputerName") as string,
                ActiveComputerName = Value(@"SYSTEM\CurrentControlSet\Control\ComputerName\ActiveComputerName", "ComputerName") as string,
                SystemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            };
            return PendingRestartEvaluator.Evaluate(facts);
        }

        public WindowsUpdateActivity GetActivity()
        {
            var names = new List<string>();
            foreach (var process in Process.GetProcesses())
            {
                try { names.Add(process.ProcessName); }
                catch (InvalidOperationException) { }
                finally { process.Dispose(); }
            }

            return ActivityEvaluator.Evaluate(names, IsRunning("TrustedInstaller"));
        }

        public NetworkReadiness CheckNetwork()
        {
            var ready = true;
            string dns, https;
            try
            {
                Dns.GetHostAddresses("download.windowsupdate.com");
                dns = "OK";
            }
            catch (Exception ex) when (ex is System.Net.Sockets.SocketException || ex is ArgumentException)
            {
                ready = false;
                dns = ex.Message;
            }

            try
            {
#pragma warning disable SYSLIB0014 // WebRequest is the HTTP API available on .NET Framework 4.8 without extra assemblies.
                var request = (HttpWebRequest)WebRequest.Create("https://www.microsoft.com/");
#pragma warning restore SYSLIB0014
                request.Method = "HEAD";
                request.Timeout = 5000;
                request.UserAgent = "WinTool by PcNinja";
                using (request.GetResponse()) { }
                https = "OK";
            }
            catch (WebException ex)
            {
                ready = false;
                https = ex.Message;
            }

            return new NetworkReadiness(ready, dns, https);
        }

        public void EnsureUpdateServicesRunning()
        {
            foreach (var name in UpdateServices) TryStart(name);
        }

        public void StopUpdateServices()
        {
            foreach (var name in new[] { "UsoSvc", "wuauserv", "bits", "dosvc", "cryptsvc" }) TryStop(name);
            System.Threading.Thread.Sleep(3000);
        }

        // IServiceControl (used by the reset)
        public void Stop(string service) => StopOrThrow(service);
        public void Start(string service) => TryStart(service);

        public bool IsRunning(string service)
        {
            try
            {
                using (var controller = new ServiceController(service)) return controller.Status == ServiceControllerStatus.Running;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                return false;
            }
        }

        /// <summary>For the health check: the service exists and is not disabled.</summary>
        public static IDictionary<string, bool> ServicesCanRun()
        {
            var result = new Dictionary<string, bool>();
            foreach (var name in new[] { "wuauserv", "bits", "cryptsvc" })
            {
                try
                {
                    using (var controller = new ServiceController(name)) result[name] = controller.StartType != ServiceStartMode.Disabled;
                }
                catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
                {
                    result[name] = false;
                }
            }

            return result;
        }

        public static long? SystemDriveFreeBytes()
        {
            try
            {
                var root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
                return new DriveInfo(root).AvailableFreeSpace;
            }
            catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        private void TryStart(string name)
        {
            try
            {
                using (var controller = new ServiceController(name))
                {
                    if (controller.Status == ServiceControllerStatus.Running || controller.StartType == ServiceStartMode.Disabled) return;
                    log.Write(EventIds.RunStarted, EventLevel.Information, "Starting service: " + name);
                    if (controller.Status != ServiceControllerStatus.StartPending) controller.Start();
                    controller.WaitForStatus(ServiceControllerStatus.Running, ServiceWait);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.ServiceProcess.TimeoutException || ex is ArgumentException)
            {
                log.Write(EventIds.WindowsBusy, EventLevel.Warning, "Service " + name + " was not started: " + ex.Message);
            }
        }

        private void TryStop(string name)
        {
            try { StopOrThrow(name); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is System.ServiceProcess.TimeoutException || ex is ArgumentException)
            {
                log.Write(EventIds.WindowsUpdateSnoozed, EventLevel.Warning, "Service " + name + " was not stopped: " + ex.Message);
            }
        }

        private static void StopOrThrow(string name)
        {
            using (var controller = new ServiceController(name))
            {
                if (controller.Status == ServiceControllerStatus.Stopped) return;
                if (controller.Status != ServiceControllerStatus.StopPending) controller.Stop();
                controller.WaitForStatus(ServiceControllerStatus.Stopped, ServiceWait);
            }
        }

        private static bool KeyExists(string path)
        {
            using (var key = Registry.LocalMachine.OpenSubKey(path)) return key != null;
        }

        private static object Value(string path, string name)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(path)) return key?.GetValue(name);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException)
            {
                return null;
            }
        }
    }

    public sealed class WindowsFolderOperations : IFolderOperations
    {
        public bool Exists(string path) => Directory.Exists(path);
        public void DeleteRecursive(string path) => Directory.Delete(path, true);
        public void Create(string path) => Directory.CreateDirectory(path);
        public void Move(string from, string to) => Directory.Move(from, to);
        public int CountEntries(string path) => Directory.Exists(path) ? Directory.GetFileSystemEntries(path).Length : 0;
    }
}
