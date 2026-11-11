using System;
using WinTool.Core.IO;

namespace WinTool.WindowsUpdate.Storage
{
    public static class LegacyDataValidation
    {
        public static string Settings(string json)
        {
            var value = JsonFileStore<WindowsUpdateSettings>.Deserialize(json);
            if (value == null || value.SchemaVersion != WindowsUpdateSettings.CurrentSchemaVersion || value.Validate().Count != 0)
                throw new FormatException("Unsupported legacy settings.");
            return JsonFileStore<WindowsUpdateSettings>.Serialize(value.Clone());
        }

        public static string State(string json)
        {
            var value = JsonFileStore<WindowsUpdateState>.Deserialize(json);
            if (value == null || value.RetryCount < 0 || value.RetryCount > 10) throw new FormatException("Invalid legacy state.");
            foreach (var stamp in new[] { value.LastRunStarted, value.LastRunFinished, value.LastSuccessfulRun, value.LastRetryScheduled })
                if (stamp != null && (stamp.Length > 40 || !WindowsUpdateState.Parse(stamp).HasValue)) throw new FormatException("Invalid legacy timestamp.");
            if (value.LastRunType != null && value.LastRunType != "Manual" && value.LastRunType != "Scheduled" && value.LastRunType != "Retry" && value.LastRunType != "Startup" && value.LastRunType != "Wake")
                throw new FormatException("Invalid legacy run type.");
            // Historical timestamps are observations, not proof of update success or registered retries.
            return JsonFileStore<WindowsUpdateState>.Serialize(new WindowsUpdateState
            {
                LastRunStarted = value.LastRunStarted, LastRunFinished = value.LastRunFinished, LastRunType = value.LastRunType,
                LastResult = "LegacyStateUnconfirmed", LastMessage = "Legacy state imported; operation outcomes require verification.",
                LastRebootRequired = value.LastRebootRequired
            });
        }
    }
}
