using System;
using System.Collections.Generic;
using WinTool.WindowsUpdate.Logic;
using WinTool.WindowsUpdate.Schedule;
using WinTool.WindowsUpdate.Storage;

namespace WinTool.WindowsUpdate.Engine
{
    /// <summary>
    /// Everything the Windows Update commands need from the machine, in one
    /// place. The real one talks to Windows; tests pass a fake.
    /// </summary>
    public interface IWindowsUpdateEnvironment
    {
        IRunLog Log { get; }
        IWindowsUpdateStore Store { get; }
        ISystemProbe Probe { get; }
        ITaskSchedulerService Tasks { get; }
        Func<DateTime> Clock { get; }
        bool IsAdministrator { get; }

        /// <summary>For status output, for example "Windows 11 Pro 24H2 (build 26100.2033)".</summary>
        string WindowsVersion { get; }

        /// <summary>%ProgramData%\PcNinja\WinTool; log packages go below it.</summary>
        string DataFolder { get; }

        /// <summary>Full path of WinTool-by-PcNinja.exe, which scheduled tasks run.</summary>
        string ExecutablePath { get; }

        /// <summary>Created on first use: starting the COM agent is slow and needs the service.</summary>
        IUpdateAgent CreateAgent();

        /// <summary>The machine-wide operation lock (WU-82); null when another operation holds it.</summary>
        IDisposable TryAcquireOperationLock();

        WindowsUpdateReset CreateReset();

        /// <summary>Health facts that need Windows (services, disk, Microsoft Update registration).</summary>
        HealthFacts CollectHealthFacts(bool checkNetwork);

        /// <summary>Text of the WinUpdate Tool V1/V2 config.json, or null when there is none (D-015).</summary>
        string ReadLegacyConfig();

        /// <summary>Folders and files to include in a log package.</summary>
        IEnumerable<string> LogSources();
    }
}
