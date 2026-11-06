using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WinTool.WindowsUpdate.Engine;

namespace WinTool.WindowsUpdate.Logic
{
    public interface IServiceControl
    {
        void Stop(string service);
        void Start(string service);
        bool IsRunning(string service);
    }

    public interface IFolderOperations
    {
        bool Exists(string path);
        void DeleteRecursive(string path);
        void Create(string path);
        void Move(string from, string to);
        int CountEntries(string path);
    }

    public sealed class ResetResult
    {
        public bool Succeeded { get; set; }
        public bool SoftwareDistributionRecreated { get; set; }
        public string Catroot2Backup { get; set; }
        public List<string> Warnings { get; } = new List<string>();
        public string Message => Succeeded
            ? "Windows Update was reset. The update cache and signature database were rebuilt and the services are running."
            : "Windows Update reset finished with warnings: " + string.Join(" ", Warnings);
    }

    /// <summary>
    /// WU-70 to WU-72: stop the services, rebuild SoftwareDistribution, move
    /// catroot2 aside (Windows rebuilds it), start the services again. Only
    /// the two exact Windows folders are ever touched.
    /// </summary>
    public sealed class WindowsUpdateReset
    {
        public static readonly string[] Services = { "UsoSvc", "wuauserv", "bits", "dosvc", "cryptsvc" };

        private readonly IServiceControl services;
        private readonly IFolderOperations folders;
        private readonly IRunLog log;
        private readonly string systemRoot;
        private readonly Func<DateTime> clock;

        public WindowsUpdateReset(IServiceControl services, IFolderOperations folders, IRunLog log, string systemRoot, Func<DateTime> clock)
        {
            this.services = services;
            this.folders = folders;
            this.log = log;
            this.systemRoot = Path.GetFullPath(systemRoot).TrimEnd('\\', '/');
            this.clock = clock ?? (() => DateTime.Now);
        }

        public string SoftwareDistributionPath => Path.Combine(systemRoot, "SoftwareDistribution");
        public string Catroot2Path => Path.Combine(Path.Combine(systemRoot, "System32"), "catroot2");

        public ResetResult Run()
        {
            var result = new ResetResult();
            log.Write(EventIds.ResetStarted, EventLevel.Warning, "Windows Update reset started.");
            StopAll(result);
            try
            {
                result.SoftwareDistributionRecreated = RebuildSoftwareDistribution(result);
                result.Catroot2Backup = MoveCatroot2(result);
            }
            finally
            {
                StartAll(result);
            }

            var notRunning = new[] { "wuauserv", "bits", "cryptsvc" }.Where(s => !SafeIsRunning(s)).ToList();
            if (notRunning.Count > 0) result.Warnings.Add("Services not running: " + string.Join(", ", notRunning) + ".");
            result.Succeeded = result.SoftwareDistributionRecreated && result.Warnings.Count == 0;
            log.Write(result.Succeeded ? EventIds.ResetFinished : EventIds.ResetFailed, result.Succeeded ? EventLevel.Information : EventLevel.Warning, result.Message);
            return result;
        }

        private bool RebuildSoftwareDistribution(ResetResult result)
        {
            var target = SoftwareDistributionPath;
            Guard(target, Path.Combine(systemRoot, "SoftwareDistribution"));
            if (!folders.Exists(target))
            {
                folders.Create(target);
                return folders.Exists(target);
            }

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    folders.DeleteRecursive(target);
                    folders.Create(target);
                    return folders.Exists(target);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    log.Write(EventIds.ResetFailed, EventLevel.Warning, "SoftwareDistribution could not be deleted (attempt " + attempt + " of 3): " + ex.Message);
                    if (attempt == 3)
                    {
                        if (!folders.Exists(target)) folders.Create(target);
                        result.Warnings.Add("SoftwareDistribution could not be fully deleted: " + ex.Message);
                        return false;
                    }

                    StopAll(result);
                }
            }

            return false;
        }

        private string MoveCatroot2(ResetResult result)
        {
            var target = Catroot2Path;
            Guard(target, Path.Combine(Path.Combine(systemRoot, "System32"), "catroot2"));
            if (!folders.Exists(target)) return null;
            var backup = target + ".bak-" + clock().ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            try
            {
                folders.Move(target, backup);
                return backup;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                result.Warnings.Add("catroot2 could not be renamed: " + ex.Message);
                return null;
            }
        }

        private void Guard(string actual, string expected)
        {
            if (!string.Equals(Path.GetFullPath(actual).TrimEnd('\\', '/'), Path.GetFullPath(expected).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Refusing to reset an unexpected path: " + actual);
            }
        }

        private void StopAll(ResetResult result)
        {
            foreach (var service in Services)
            {
                try { services.Stop(service); }
                catch (Exception ex) when (!(ex is OutOfMemoryException)) { log.Write(EventIds.ResetFailed, EventLevel.Warning, "Could not stop " + service + ": " + ex.Message); }
            }
        }

        private void StartAll(ResetResult result)
        {
            foreach (var service in Enumerable.Reverse(Services))
            {
                try { services.Start(service); }
                catch (Exception ex) when (!(ex is OutOfMemoryException)) { log.Write(EventIds.ResetFailed, EventLevel.Warning, "Could not start " + service + ": " + ex.Message); }
            }
        }

        private bool SafeIsRunning(string service)
        {
            try { return services.IsRunning(service); }
            catch (Exception ex) when (!(ex is OutOfMemoryException)) { return false; }
        }
    }
}
