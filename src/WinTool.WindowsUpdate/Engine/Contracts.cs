using System;
using System.Collections.Generic;
using System.Threading;
using WinTool.WindowsUpdate.Logic;
using WinTool.WindowsUpdate.Model;

namespace WinTool.WindowsUpdate.Engine
{
    /// <summary>Windows Update Agent result codes (OperationResultCode).</summary>
    public enum OperationResult
    {
        NotStarted = 0,
        InProgress = 1,
        Succeeded = 2,
        SucceededWithErrors = 3,
        Failed = 4,
        Aborted = 5
    }

    /// <summary>A Windows Update failure with its HRESULT.</summary>
    public sealed class UpdateAgentException : Exception
    {
        public UpdateAgentException(string message, int hresult, Exception inner = null)
            : base(message, inner)
        {
            HResult = hresult;
        }

        public string Describe() => HResults.Describe(Message, HResult);
    }

    public sealed class ItemResult
    {
        public ItemResult(UpdateItem update, OperationResult result, int hresult)
        {
            Update = update;
            Result = result;
            HResult = hresult;
        }

        public UpdateItem Update { get; }
        public OperationResult Result { get; }
        public int HResult { get; }
        public bool Succeeded => Result == OperationResult.Succeeded || Result == OperationResult.SucceededWithErrors;
    }

    public sealed class InstallOutcome
    {
        public InstallOutcome(OperationResult result, bool rebootRequired, IReadOnlyList<ItemResult> items)
        {
            Result = result;
            RebootRequired = rebootRequired;
            Items = items;
        }

        public OperationResult Result { get; }
        public bool RebootRequired { get; }
        public IReadOnlyList<ItemResult> Items { get; }
    }

    /// <summary>The Windows Update Agent. The real one wraps COM; tests use a fake.</summary>
    public interface IUpdateAgent
    {
        /// <summary>WU-02. Returns false (and logs) when registration fails; never throws.</summary>
        bool EnsureMicrosoftUpdateRegistered();

        /// <summary>Throws <see cref="UpdateAgentException"/> on failure.</summary>
        IReadOnlyList<UpdateItem> Search(string criteria);

        void AcceptEula(UpdateItem update);

        /// <summary>Downloads one update, so progress and cancel work per update (WU-24).</summary>
        ItemResult Download(UpdateItem update, CancellationToken cancellation);

        /// <summary>Installs as one batch so Windows orders prerequisites; per-update results (WU-25).</summary>
        InstallOutcome Install(IReadOnlyList<UpdateItem> updates);
    }

    public sealed class NetworkReadiness
    {
        public NetworkReadiness(bool ready, string dns, string https)
        {
            Ready = ready;
            Dns = dns;
            Https = https;
        }

        public bool Ready { get; }
        public string Dns { get; }
        public string Https { get; }
        public string Message => "DNS: " + Dns + " | HTTPS: " + Https;
    }

    /// <summary>Machine facts and service control used around a run.</summary>
    public interface ISystemProbe
    {
        PendingRestartState GetPendingRestart();
        WindowsUpdateActivity GetActivity();
        NetworkReadiness CheckNetwork();

        /// <summary>WU-33: start the update services that are stopped.</summary>
        void EnsureUpdateServicesRunning();

        /// <summary>WU-31 "snooze": stop the update services (they are started again by EnsureUpdateServicesRunning).</summary>
        void StopUpdateServices();
    }

    /// <summary>The one-time retry task (WU-62).</summary>
    public interface IRetryScheduler
    {
        void ScheduleRetry(DateTime runAt, string reason, int retryNumber);
        void CancelRetry();
    }

    public enum EventLevel
    {
        Information,
        Warning,
        Error
    }

    /// <summary>Writes to the WinTool log and the Windows event log (WU-83).</summary>
    public interface IRunLog
    {
        void Write(int eventId, EventLevel level, string message);
    }
}
