using System.Collections.Generic;
using System.Linq;

namespace WinTool.WindowsUpdate.Logic
{
    public enum HealthStatus
    {
        Ok,
        Warning,
        Problem
    }

    public sealed class HealthItem
    {
        public HealthItem(string id, HealthStatus status, string detail)
        {
            Id = id;
            Status = status;
            Detail = detail;
        }

        /// <summary>Stable id; the UI maps it to translated text.</summary>
        public string Id { get; }
        public HealthStatus Status { get; }

        /// <summary>English detail for logs and the CLI.</summary>
        public string Detail { get; }
    }

    public sealed class HealthFacts
    {
        public IDictionary<string, bool> ServicesCanRun { get; set; } = new Dictionary<string, bool>();
        public long? SystemDriveFreeBytes { get; set; }
        public PendingRestartState PendingRestart { get; set; } = PendingRestartState.None;
        public WindowsUpdateActivity Activity { get; set; } = WindowsUpdateActivity.Idle;
        public bool? NetworkReady { get; set; }
        public bool? MicrosoftUpdateRegistered { get; set; }

        /// <summary>Null when no schedule is enabled.</summary>
        public bool? ScheduledTaskMatchesSettings { get; set; }
    }

    /// <summary>WU-95: a real health check, replacing V2's fixed "Healthy".</summary>
    public static class HealthCheck
    {
        public const long WarnBelowBytes = 20L * 1024 * 1024 * 1024;
        public const long ProblemBelowBytes = 8L * 1024 * 1024 * 1024;

        public static IReadOnlyList<HealthItem> Evaluate(HealthFacts facts)
        {
            var items = new List<HealthItem>();

            var broken = facts.ServicesCanRun.Where(s => !s.Value).Select(s => s.Key).ToList();
            items.Add(broken.Count == 0
                ? new HealthItem("services", HealthStatus.Ok, "Update services are available.")
                : new HealthItem("services", HealthStatus.Problem, "Update services disabled or missing: " + string.Join(", ", broken) + "."));

            if (facts.SystemDriveFreeBytes.HasValue)
            {
                var free = facts.SystemDriveFreeBytes.Value;
                var gb = (free / (1024.0 * 1024 * 1024)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " GB free";
                items.Add(new HealthItem("disk", free < ProblemBelowBytes ? HealthStatus.Problem : free < WarnBelowBytes ? HealthStatus.Warning : HealthStatus.Ok, gb));
            }

            items.Add(facts.PendingRestart.Pending
                ? new HealthItem("restart", HealthStatus.Warning, "Restart pending: " + string.Join("; ", facts.PendingRestart.Reasons))
                : new HealthItem("restart", HealthStatus.Ok, "No restart pending."));

            items.Add(facts.Activity.Status == ActivityStatus.Idle
                ? new HealthItem("activity", HealthStatus.Ok, facts.Activity.Message)
                : new HealthItem("activity", HealthStatus.Warning, facts.Activity.Message));

            if (facts.NetworkReady.HasValue)
            {
                items.Add(new HealthItem("network", facts.NetworkReady.Value ? HealthStatus.Ok : HealthStatus.Problem,
                    facts.NetworkReady.Value ? "Windows Update servers are reachable." : "Windows Update servers are not reachable."));
            }

            if (facts.MicrosoftUpdateRegistered.HasValue)
            {
                items.Add(new HealthItem("microsoft-update", facts.MicrosoftUpdateRegistered.Value ? HealthStatus.Ok : HealthStatus.Warning,
                    facts.MicrosoftUpdateRegistered.Value ? "Microsoft Update is enabled." : "Microsoft Update is not enabled; Office and other Microsoft products may not be offered."));
            }

            if (facts.ScheduledTaskMatchesSettings.HasValue)
            {
                items.Add(new HealthItem("schedule", facts.ScheduledTaskMatchesSettings.Value ? HealthStatus.Ok : HealthStatus.Problem,
                    facts.ScheduledTaskMatchesSettings.Value ? "The scheduled task matches the settings." : "The schedule is on, but the scheduled task is missing or different. Save the schedule again."));
            }

            return items;
        }

        public static HealthStatus Overall(IEnumerable<HealthItem> items)
        {
            var list = items.ToList();
            if (list.Any(i => i.Status == HealthStatus.Problem)) return HealthStatus.Problem;
            return list.Any(i => i.Status == HealthStatus.Warning) ? HealthStatus.Warning : HealthStatus.Ok;
        }
    }
}
