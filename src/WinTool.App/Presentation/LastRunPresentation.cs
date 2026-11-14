using System;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Security;
using WinTool.WindowsUpdate.Engine;
using WinTool.WindowsUpdate.Storage;

namespace WinTool.App.Presentation
{
    /// <summary>Saved history only; never a live scan or proof of current machine state.</summary>
    public sealed class LastRunPresentation
    {
        private LastRunPresentation(DateTime? stamp, string type, string outcome)
        { Stamp = stamp; TypeKey = type; OutcomeKey = outcome; }
        public DateTime? Stamp { get; }
        public string TypeKey { get; }
        public string OutcomeKey { get; }
        public static LastRunPresentation Empty { get; } = new LastRunPresentation(null, null, null);

        public static LastRunPresentation Read(Func<WindowsUpdateState> read)
        {
            try
            {
                var state = read();
                if (state == null || !Enum.TryParse(state.LastRunType, true, out RunType type) || !Enum.IsDefined(typeof(RunType), type) ||
                    !string.Equals(type.ToString(), state.LastRunType, StringComparison.OrdinalIgnoreCase)) return Empty;
                var running = string.Equals(state.LastResult, "Running", StringComparison.OrdinalIgnoreCase);
                var outcome = running ? "History_Result_Incomplete" : ResultKey(state.LastResult);
                var stamp = WindowsUpdateState.Parse(running ? state.LastRunStarted : state.LastRunFinished);
                if (outcome == null || !stamp.HasValue) return Empty;
                var started = WindowsUpdateState.Parse(state.LastRunStarted);
                if (!running && started.HasValue && started.Value > stamp.Value) return Empty;
                return new LastRunPresentation(stamp, "History_Type_" + type, outcome);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SecurityException ||
                ex is SerializationException || ex is FormatException || ex is InvalidCastException || ex is OverflowException)
            { return Empty; }
        }

        public string Format(Func<string, string> text, CultureInfo culture) => !Stamp.HasValue ? text("History_Empty") :
            string.Format(culture, text("History_Value"), Stamp.Value.ToString("g", culture), text(TypeKey), text(OutcomeKey));

        private static string ResultKey(string value)
        {
            if (!Enum.TryParse(value, true, out RunResult result) || !Enum.IsDefined(typeof(RunResult), result) ||
                !string.Equals(result.ToString(), value, StringComparison.OrdinalIgnoreCase)) return null;
            switch (result)
            {
                case RunResult.Succeeded: return "History_Result_Success";
                case RunResult.SucceededWithErrors: return "History_Result_Partial";
                case RunResult.NoUpdates: return "History_Result_NoUpdates";
                case RunResult.RebootRequired: return "History_Result_Reboot";
                case RunResult.RetryScheduled: return "History_Result_Retry";
                case RunResult.MaxPassesReached:
                case RunResult.MaxRetriesReached:
                case RunResult.RetryDisabled: return "History_Result_Attention";
                case RunResult.SkippedInstalling:
                case RunResult.SkippedBackgroundActivity:
                case RunResult.CooldownActive:
                case RunResult.AlreadyRunning: return "History_Result_Skipped";
                case RunResult.Cancelled: return "History_Result_Cancelled";
                case RunResult.SelectionUnavailable: return "History_Result_Unavailable";
                case RunResult.Failed:
                case RunResult.DownloadFailed:
                case RunResult.NoDownloadedUpdates: return "History_Result_Failed";
                default: return null;
            }
        }
    }
}
