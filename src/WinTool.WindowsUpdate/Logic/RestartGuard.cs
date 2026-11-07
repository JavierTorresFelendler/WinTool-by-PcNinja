using System;

namespace WinTool.WindowsUpdate.Logic
{
    public enum RestartDecision
    {
        /// <summary>Nothing else runs; the caller holds the operation lock until Windows restarts.</summary>
        Allowed,

        /// <summary>Another WinTool operation runs (a scheduled or command-line update, an upgrade, Office).</summary>
        OperationRunning,

        /// <summary>Windows itself is installing (TrustedInstaller, TiWorker...): ask before restarting.</summary>
        WindowsInstalling
    }

    /// <summary>
    /// Whether the window may restart the computer now (owner VM test, Codex triage item 3):
    /// never during another WinTool operation, which the machine-wide lock reveals even when it
    /// runs in a scheduled task; and only with a warning while Windows' own servicing installs.
    /// </summary>
    public static class RestartGuard
    {
        public static RestartDecision Evaluate(Func<IDisposable> acquireLock, Func<bool> otherOperationRunning,
            Func<WindowsUpdateActivity> activity, out IDisposable lease)
        {
            lease = acquireLock();
            if (lease == null) return RestartDecision.OperationRunning;
            if (otherOperationRunning())
            {
                lease.Dispose();
                lease = null;
                return RestartDecision.OperationRunning;
            }

            return activity().IsInstalling ? RestartDecision.WindowsInstalling : RestartDecision.Allowed;
        }
    }
}
