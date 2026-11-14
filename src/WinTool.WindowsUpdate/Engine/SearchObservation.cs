using System;
using System.Collections.Generic;
using System.Diagnostics;
using WinTool.WindowsUpdate.Model;

namespace WinTool.WindowsUpdate.Engine
{
    /// <summary>Requested/reported settings only; not proof of the contacted endpoint.</summary>
    public sealed class SearchContext
    {
        public SearchContext(int? server, string service, bool serviceKnown, bool? online, bool? mu)
        { ServerSelection = server; ServiceId = service; ServiceIdKnown = serviceKnown; Online = online; MuRegistered = mu; }
        public int? ServerSelection { get; }
        public string ServiceId { get; }
        public bool ServiceIdKnown { get; }
        public bool? Online { get; }
        public bool? MuRegistered { get; }
        public bool Known => ServerSelection.HasValue && ServiceIdKnown && Online.HasValue && MuRegistered.HasValue;
        public bool Matches(SearchContext other) => Known && other != null && other.Known &&
            ServerSelection == other.ServerSelection && string.Equals(ServiceId, other.ServiceId, StringComparison.OrdinalIgnoreCase) &&
            Online == other.Online && MuRegistered == other.MuRegistered;
        public static SearchContext Unknown => new SearchContext(null, null, false, null, null);
        public override string ToString() => "known=" + Known + ";server=" + ServerSelection + ";service=" + ServiceId + ";serviceKnown=" + ServiceIdKnown + ";online=" + Online + ";mu=" + MuRegistered;
    }

    public interface IObservedSearchAgent
    {
        IPreparedUpdateSearch PrepareSearch();
    }
    public interface IPreparedUpdateSearch
    {
        SearchContext Context { get; }
        IReadOnlyList<UpdateItem> Search(string criteria);
        SearchContext ReadContext();
    }
    public class QueryClock
    {
        public virtual DateTime UtcNow => DateTime.UtcNow;
        public virtual long Timestamp => Stopwatch.GetTimestamp();
        public virtual long Frequency => Stopwatch.Frequency;
    }
    public sealed class SearchEvidence
    {
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public string BatchId { get; internal set; }
        public string Source { get; internal set; }
        public string Criteria { get; internal set; }
        public SearchContext Before { get; internal set; } = SearchContext.Unknown;
        public SearchContext After { get; internal set; } = SearchContext.Unknown;
        public IReadOnlyList<UpdateItem> Updates { get; internal set; } = new List<UpdateItem>();
        public UpdateAgentException Error { get; internal set; }
        internal bool Completed { get; set; }
        public bool Succeeded => Completed && Error == null;
    }
    public sealed class SelectionEntry
    {
        public SelectionEntry(UpdateItem update, string primary, SearchContext context)
        { Key = update.Key; UpdateId = update.UpdateId; Revision = update.Revision; Primary = primary; Context = context; }
        public string Key { get; }
        public string UpdateId { get; }
        public int Revision { get; }
        public string Primary { get; }
        public SearchContext Context { get; }
    }
    public sealed class SelectionSnapshot
    {
        public SelectionSnapshot(IEnumerable<SelectionEntry> entries, UpdateScope scope)
        { Entries = new List<SelectionEntry>(entries).AsReadOnly(); Scope = scope; }
        public IReadOnlyList<SelectionEntry> Entries { get; }
        public UpdateScope Scope { get; }
    }
    public enum SelectionSkipReason { ChangedRevision, NoLongerOffered, Policy }
    public sealed class SelectionSkip
    {
        public SelectionSkip(string key, SelectionSkipReason reason) { Key = key; Reason = reason; }
        public string Key { get; }
        public SelectionSkipReason Reason { get; }
    }
}
