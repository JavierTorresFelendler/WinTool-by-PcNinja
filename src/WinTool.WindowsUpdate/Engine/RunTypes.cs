using System;
using System.Collections.Generic;
using WinTool.WindowsUpdate.Model;

namespace WinTool.WindowsUpdate.Engine
{
    public enum RunType
    {
        Manual,
        Scheduled,
        Retry,
        Startup,
        Wake
    }

    public enum RunResult
    {
        Succeeded,
        SucceededWithErrors,
        NoUpdates,
        RebootRequired,
        MaxPassesReached,
        RetryScheduled,
        MaxRetriesReached,
        RetryDisabled,
        SkippedInstalling,
        SkippedBackgroundActivity,
        CooldownActive,
        AlreadyRunning,
        Cancelled,
        DownloadFailed,
        NoDownloadedUpdates,
        Failed,
        SelectionUnavailable
    }

    /// <summary>What Windows is doing when a manual run starts, and what the user chose (WU-31).</summary>
    public enum BusyDecision
    {
        Cancel,
        RetryLater,
        Snooze
    }

    public sealed class RunRequest
    {
        public RunType Type { get; set; } = RunType.Manual;
        public UpdateScope Scope { get; set; }

        /// <summary>WU-27: when set, only these update keys are installed, in one pass.</summary>
        public ISet<string> SelectedKeys { get; set; }
        public bool SelectionWorkflow { get; set; }
        public SelectionSnapshot Selection { get; set; }

        /// <summary>Manual runs only: asked when Windows Update is busy. Null = treat as RetryLater.</summary>
        public Func<Logic.WindowsUpdateActivity, BusyDecision> OnWindowsBusy { get; set; }

        public int MaxPasses { get; set; } = 3;
    }

    public enum RunStage
    {
        Checking,
        Searching,
        Downloading,
        Installing,
        Verifying,
        Finished
    }

    public sealed class RunProgress
    {
        public RunProgress(RunStage stage, int pass, int current, int total, string title)
        {
            Stage = stage;
            Pass = pass;
            Current = current;
            Total = total;
            Title = title;
        }

        public RunStage Stage { get; }
        public int Pass { get; }
        public int Current { get; }
        public int Total { get; }
        public string Title { get; }
    }

    public sealed class RunOutcome
    {
        public RunOutcome(RunResult result, string message)
        {
            Result = result;
            Message = message;
            Items = new List<ItemResult>();
        }

        public RunResult Result { get; set; }
        public string Message { get; set; }
        public int UpdatesFound { get; set; }
        public int PassesCompleted { get; set; }
        public bool RebootRequired { get; set; }
        public DateTime? RetryAt { get; set; }
        public ScanResult PostScan { get; set; }
        public bool VerificationUnknown { get; set; }
        public List<SelectionSkip> SelectionSkipped { get; } = new List<SelectionSkip>();

        /// <summary>Per-update download and install results across passes (WU-25).</summary>
        public List<ItemResult> Items { get; }
    }
}
