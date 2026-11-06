using System;

namespace WinTool.Core.Updates
{
    /// <summary>
    /// The last check before an update replaces WinTool or starts its installer
    /// (Codex review of phase 6, PR #2). It takes WinTool's machine-wide operation lock,
    /// which a scheduled task or the command line may hold, and then confirms that no
    /// page in this window is busy. The caller keeps the returned lease until WinTool
    /// exits, so nothing else can start in between. Null means: do not update now.
    /// </summary>
    public static class UpdateGate
    {
        public static IDisposable TryEnter(Func<IDisposable> acquireLock, Func<bool> otherOperationRunning)
        {
            if (acquireLock == null) throw new ArgumentNullException(nameof(acquireLock));
            if (otherOperationRunning == null) throw new ArgumentNullException(nameof(otherOperationRunning));
            var lease = acquireLock();
            if (lease == null) return null;
            if (!otherOperationRunning()) return lease;
            lease.Dispose();
            return null;
        }
    }
}
