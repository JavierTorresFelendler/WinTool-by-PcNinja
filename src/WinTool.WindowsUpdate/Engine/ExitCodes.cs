namespace WinTool.WindowsUpdate.Engine
{
    /// <summary>
    /// Command-line exit codes for update runs (WU-90). Each kind of outcome
    /// has its own code, so scripts and monitoring tools can tell them apart.
    /// </summary>
    public static class ExitCodes
    {
        public const int Ok = 0;
        public const int Failed = 1;
        public const int UsageError = 2;
        public const int Cancelled = 3;
        public const int RebootRequired = 10;
        public const int CompletedWithErrors = 11;
        public const int RetryScheduled = 20;
        public const int Skipped = 21;
        public const int NeedsAttention = 22;

        public static int For(RunResult result)
        {
            switch (result)
            {
                case RunResult.Succeeded:
                case RunResult.NoUpdates:
                case RunResult.MaxPassesReached:
                    return Ok;
                case RunResult.RebootRequired:
                    return RebootRequired;
                case RunResult.SucceededWithErrors:
                    return CompletedWithErrors;
                case RunResult.RetryScheduled:
                    return RetryScheduled;
                case RunResult.SkippedInstalling:
                case RunResult.SkippedBackgroundActivity:
                case RunResult.CooldownActive:
                case RunResult.AlreadyRunning:
                    return Skipped;
                case RunResult.MaxRetriesReached:
                case RunResult.RetryDisabled:
                    return NeedsAttention;
                case RunResult.Cancelled:
                    return Cancelled;
                default:
                    return Failed;
            }
        }
    }
}
