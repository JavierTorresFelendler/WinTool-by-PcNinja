using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        public IReadOnlyList<SearchEvidence> Evidence { get; internal set; } = new List<SearchEvidence>();
        public bool Complete => Evidence.Count == 3 && Evidence.All(e => e.Succeeded);
        public SelectionSnapshot Select(ISet<string> keys) => new SelectionSnapshot(Updates.Where(u => keys.Contains(u.Update.Key)).Select(u =>
            new SelectionEntry(u.Update, u.Update.PrimarySource, DiscoveryContext(u.Update.PrimarySource))), Scope);
        private SearchContext DiscoveryContext(string source)
        {
            var query = Evidence.FirstOrDefault(e => e.Source == source);
            return query != null && query.Succeeded && query.Before.Matches(query.After) ? query.After : SearchContext.Unknown;
        }
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
        private readonly QueryClock clock;
        private readonly string batchId = Guid.NewGuid().ToString("N");
        public static IReadOnlyList<string> QuerySources { get; } = Array.AsReadOnly(new[] { "Broad", "Driver", "Optional" });

        public ScanService(IUpdateAgent agent, IRunLog log, QueryClock clock = null)
        {
            this.agent = agent;
            this.log = log;
            this.clock = clock ?? new QueryClock();
        }

        public ScanResult Scan(UpdateScope scope, ISet<string> selectedKeys = null, string label = "Scan")
        {
            var searches = new List<KeyValuePair<string, IReadOnlyList<UpdateItem>>>();
            var errors = new List<UpdateAgentException>();
            var evidence = new List<SearchEvidence>();
            foreach (var search in new[] { ("Broad", BroadCriteria), ("Driver", DriverCriteria), ("Optional", OptionalCriteria) })
            {
                try
                {
                    var query = Query(search.Item1);
                    evidence.Add(query);
                    if (query.Error != null) throw query.Error;
                    var found = query.Updates;
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

            return new ScanResult(scanned, scope) { Evidence = evidence.AsReadOnly() };
        }

        public SearchEvidence Query(string source, Func<SearchContext, string> prepared = null)
        {
            var result = new SearchEvidence { BatchId = batchId, Source = source, Criteria = Criteria(source) };
            var start = clock.Timestamp;
            log.Write(EventIds.SearchQueryStarted, EventLevel.Information, "Query begin " + result.Id + "; batch=" + batchId + "; requestedClass=" + source + "; utc=" + clock.UtcNow.ToString("O") + "; tick=" + start + "; frequency=" + clock.Frequency);
            try
            {
                var query = (agent as IObservedSearchAgent)?.PrepareSearch();
                result.Before = query?.Context ?? SearchContext.Unknown;
                if (prepared != null)
                {
                    result.Source = prepared(result.Before);
                    result.Criteria = Criteria(result.Source);
                }
                result.Updates = query == null ? agent.Search(result.Criteria) : query.Search(result.Criteria);
                result.After = query?.ReadContext() ?? SearchContext.Unknown;
                result.Completed = true;
            }
            catch (UpdateAgentException ex) { result.Error = ex; }
            finally
            {
                var end = clock.Timestamp;
                if (result.Error != null) log.Write(EventIds.SearchFailed, EventLevel.Warning, "Query failed " + result.Id + "; class=" + result.Source + "; " + result.Error.Describe());
                log.Write(EventIds.SearchQueryFinished, EventLevel.Information, "Query end " + result.Id + "; batch=" + batchId + "; class=" + result.Source + "; criteria=" + result.Criteria + "; utc=" + clock.UtcNow.ToString("O") + "; tick=" + end + "; elapsedMs=" + ((end - start) * 1000.0 / clock.Frequency).ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "; count=" + result.Updates.Count + "; success=" + result.Succeeded + "; before=" + result.Before + "; after=" + result.After);
            }
            return result;
        }

        public ScanResult Revalidate(UpdateScope scope, ISet<string> keys, SelectionSnapshot selection, CancellationToken token)
        {
            var entries = selection?.Entries;
            var reduced = entries != null && entries.Count == keys.Count && entries.All(e => keys.Contains(e.Key) && QuerySources.Contains(e.Primary) && e.Context?.Known == true) &&
                selection.Scope.ToString() == scope.ToString();
            var planned = new HashSet<string>(reduced ? entries.Select(e => e.Primary) : QuerySources);
            var queries = new Dictionary<string, SearchEvidence>();
            log.Write(EventIds.SearchPlan, EventLevel.Information, "Revalidation plan: " + (reduced ? "primary sources" : "full fallback: missing/unknown discovery metadata or changed scope"));
            while (planned.Any(p => !queries.ContainsKey(p)))
            {
                token.ThrowIfCancellationRequested();
                var source = QuerySources.First(p => planned.Contains(p) && !queries.ContainsKey(p));
                Func<SearchContext, string> check = context =>
                {
                    token.ThrowIfCancellationRequested();
                    if (!reduced || !entries.Where(e => e.Primary == source).All(e => e.Context.Matches(context)))
                    {
                        reduced = false;
                        foreach (var name in QuerySources) planned.Add(name);
                        log.Write(EventIds.SearchPlan, EventLevel.Warning, "Full validation fallback: context mismatch/unknown; retain completed classes, no retry.");
                    }
                    return QuerySources.First(p => planned.Contains(p) && !queries.ContainsKey(p));
                };
                var query = Query(source, check);
                queries.Add(query.Source, query);
                if (query.Error != null) throw query.Error;
                if (!query.Before.Matches(query.After) || queries.Values.Any(q => !q.After.Matches(query.After)))
                {
                    reduced = false;
                    foreach (var name in QuerySources) planned.Add(name);
                    log.Write(EventIds.SearchPlan, EventLevel.Warning, "Validation context unstable/unknown; complete full batch with fresh results (RC3 exposure).");
                }
            }
            token.ThrowIfCancellationRequested();
            return Build(scope, keys, QuerySources.Where(queries.ContainsKey).Select(s => queries[s]).ToList(), selection);
        }

        public ScanResult CompleteSnapshot(UpdateScope scope, SearchEvidence broad)
        {
            return Build(scope, null, new[] { broad, Query("Driver"), Query("Optional") });
        }

        private static ScanResult Build(UpdateScope scope, ISet<string> keys, IReadOnlyList<SearchEvidence> evidence, SelectionSnapshot selection = null)
        {
            var merged = UpdateMerger.Merge(evidence.Where(e => e.Succeeded).Select(e => new KeyValuePair<string, IReadOnlyList<UpdateItem>>(e.Source, e.Updates)));
            var updates = merged.Select(u =>
            {
                var original = selection?.Entries.FirstOrDefault(e => e.Key == u.Key);
                var excluded = UpdateClassifier.IsFirmware(u) && !scope.Firmware ? ExclusionReason.Firmware : !UpdateClassifier.IsIncluded(u, scope) ? ExclusionReason.Scope :
                    keys != null && (!keys.Contains(u.Key) || original != null && original.Revision != u.Revision) ? ExclusionReason.NotSelected : ExclusionReason.None;
                return new ScannedUpdate(u, UpdateClassifier.KindOf(u), UpdateClassifier.IsFirmware(u), excluded);
            }).ToList();
            return new ScanResult(updates, scope) { Evidence = evidence };
        }
        public static string Criteria(string source) => source == "Broad" ? BroadCriteria : source == "Driver" ? DriverCriteria : source == "Optional" ? OptionalCriteria : throw new ArgumentException("Unknown query class.", nameof(source));
    }
}
