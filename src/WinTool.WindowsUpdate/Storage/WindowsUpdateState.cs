using System;
using System.Runtime.Serialization;

namespace WinTool.WindowsUpdate.Storage
{
    /// <summary>What happened in the last runs (WU-81). Times are local ISO 8601 text, as in V2.</summary>
    [DataContract]
    public sealed class WindowsUpdateState
    {
        [DataMember(Name = "lastRunStarted", Order = 0)] public string LastRunStarted { get; set; }
        [DataMember(Name = "lastRunFinished", Order = 1)] public string LastRunFinished { get; set; }
        [DataMember(Name = "lastRunType", Order = 2)] public string LastRunType { get; set; }
        [DataMember(Name = "lastResult", Order = 3)] public string LastResult { get; set; }
        [DataMember(Name = "lastMessage", Order = 4)] public string LastMessage { get; set; }
        [DataMember(Name = "lastSuccessfulRun", Order = 5)] public string LastSuccessfulRun { get; set; }
        [DataMember(Name = "lastRetryScheduled", Order = 6)] public string LastRetryScheduled { get; set; }
        [DataMember(Name = "lastRetryReason", Order = 7)] public string LastRetryReason { get; set; }
        [DataMember(Name = "retryCount", Order = 8)] public int RetryCount { get; set; }
        [DataMember(Name = "lastRebootRequired", Order = 9)] public bool LastRebootRequired { get; set; }

        public static string Stamp(DateTime time) => time.ToString("s", System.Globalization.CultureInfo.InvariantCulture);

        public static DateTime? Parse(string value)
        {
            DateTime parsed;
            return DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out parsed)
                ? parsed
                : (DateTime?)null;
        }
    }
}
