using System.Collections.Generic;
using System.Linq;
using WinTool.WindowsUpdate.Logic;
using WinTool.WindowsUpdate.Model;

namespace WinTool.WindowsUpdate.Engine
{
    public enum ExclusionReason
    {
        None,
        Firmware,
        Scope,
        NotSelected
    }

    public sealed class ScannedUpdate
    {
        public ScannedUpdate(UpdateItem update, UpdateKind kind, bool isFirmware, ExclusionReason excluded)
        {
            Update = update;
            Kind = kind;
            IsFirmware = isFirmware;
            Excluded = excluded;
        }

        public UpdateItem Update { get; }
        public UpdateKind Kind { get; }
        public bool IsFirmware { get; }
        public ExclusionReason Excluded { get; }
        public bool Included => Excluded == ExclusionReason.None;
    }

    public sealed class ScanResult
    {
        public ScanResult(IReadOnlyList<ScannedUpdate> updates, UpdateScope scope)
        {
            Updates = updates;
            Scope = scope;
        }

        public IReadOnlyList<ScannedUpdate> Updates { get; }
        public UpdateScope Scope { get; }
        public int Important => Updates.Count(u => u.Included && !u.IsFirmware && u.Kind == UpdateKind.Windows);
        public int Optional => Updates.Count(u => u.Included && !u.IsFirmware && u.Kind == UpdateKind.Optional);

        /// <summary>Drivers include firmware that is in scope, as in V2 (WU-08).</summary>
        public int Drivers => Updates.Count(u => u.Included && (u.Kind == UpdateKind.Driver || u.IsFirmware));
        public int FirmwareSkipped => Updates.Count(u => u.Excluded == ExclusionReason.Firmware);
        public int ScopeSkipped => Updates.Count(u => u.Excluded == ExclusionReason.Scope);
        public int Total => Updates.Count(u => u.Included);
        public IEnumerable<UpdateItem> IncludedUpdates => Updates.Where(u => u.Included).Select(u => u.Update);
    }

    /// <summary>WU-03, WU-08: three searches, merged, classified. Installs nothing.</summary>
    public sealed class ScanService
    {
        public const string BroadCriteria = "IsInstalled=0 and IsHidden=0";
        public const string DriverCriteria = "IsInstalled=0 and IsHidden=0 and Type='Driver'";
        public const string OptionalCriteria = "IsInstalled=0 and IsHidden=0 and BrowseOnly=1";

        private readonly IUpdateAgent agent;
        private readonly IRunLog log;

        public ScanService(IUpdateAgent agent, IRunLog log)
        {
            this.agent = agent;
            this.log = log;
        }

        public ScanResult Scan(UpdateScope scope, ISet<string> selectedKeys = null, string label = "Scan")
        {
            var searches = new List<KeyValuePair<string, IReadOnlyList<UpdateItem>>>();
            var errors = new List<UpdateAgentException>();
            foreach (var search in new[] { ("Broad", BroadCriteria), ("Driver", DriverCriteria), ("Optional", OptionalCriteria) })
            {
                try
                {
                    var found = agent.Search(search.Item2);
                    log.Write(EventIds.SearchResult, EventLevel.Information, label + " " + search.Item1.ToLowerInvariant() + " search found " + found.Count + " update(s).");
                    searches.Add(new KeyValuePair<string, IReadOnlyList<UpdateItem>>(search.Item1, found));
                }
                catch (UpdateAgentException ex)
                {
                    errors.Add(ex);
                    log.Write(EventIds.SearchFailed, EventLevel.Warning, label + " " + search.Item1.ToLowerInvariant() + " search failed. Criteria: " + search.Item2 + ". " + ex.Describe());
                }
            }

            if (searches.Count == 0)
            {
                var first = errors[0];
                throw new UpdateAgentException("Windows Update search returned no usable results. " + string.Join(" | ", errors.Select(e => e.Describe())), first.HResult, first);
            }

            var merged = UpdateMerger.Merge(searches);
            var scanned = new List<ScannedUpdate>();
            foreach (var update in merged)
            {
                var firmware = UpdateClassifier.IsFirmware(update);
                var reason = ExclusionReason.None;
                if (firmware && !scope.Firmware) reason = ExclusionReason.Firmware;
                else if (!UpdateClassifier.IsIncluded(update, scope)) reason = ExclusionReason.Scope;
                else if (selectedKeys != null && !selectedKeys.Contains(update.Key)) reason = ExclusionReason.NotSelected;

                scanned.Add(new ScannedUpdate(update, UpdateClassifier.KindOf(update), firmware, reason));
                log.Write(EventIds.UpdateFound, EventLevel.Information, label + " found: " + UpdateClassifier.Describe(update));
            }

            return new ScanResult(scanned, scope);
        }
    }
}
