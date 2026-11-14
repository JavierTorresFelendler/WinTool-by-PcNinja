using System.Collections.Generic;

namespace WinTool.WindowsUpdate.Model
{
    public enum UpdateKind
    {
        /// <summary>Important Windows and Microsoft updates.</summary>
        Windows,
        /// <summary>Updates Windows offers but does not select (BrowseOnly).</summary>
        Optional,
        Driver
    }

    /// <summary>One update as Windows Update describes it, independent of COM.</summary>
    public sealed class UpdateItem
    {
        public UpdateItem(string updateId, int revision, string title)
        {
            UpdateId = updateId ?? string.Empty;
            Revision = revision;
            Title = title ?? string.Empty;
            KbArticles = new List<string>();
            Categories = new List<string>();
            Sources = new List<string>();
        }

        public string UpdateId { get; }
        public int Revision { get; }
        public string Title { get; }
        public List<string> KbArticles { get; }
        public List<string> Categories { get; }

        /// <summary>Windows Update reports type 2 for drivers.</summary>
        public bool IsDriverType { get; set; }
        public bool BrowseOnly { get; set; }
        public bool IsMandatory { get; set; }
        public bool EulaAccepted { get; set; }
        public bool IsDownloaded { get; set; }

        /// <summary>Driver class for driver updates, for example "Firmware".</summary>
        public string DriverClass { get; set; }
        public long MaxDownloadSize { get; set; }

        /// <summary>Which searches found it (Broad, Driver, Optional) - WU-04.</summary>
        public List<string> Sources { get; }
        public string PrimarySource { get; internal set; }

        /// <summary>The native update object; only the agent that created it reads it.</summary>
        internal object Native { get; set; }

        /// <summary>WU-04: UpdateID and revision, or type and title when Windows gives no id.</summary>
        public string Key
        {
            get
            {
                return string.IsNullOrEmpty(UpdateId)
                    ? "fallback|" + (IsDriverType ? "Driver" : "Software") + "|" + Title
                    : UpdateId + "|" + Revision;
            }
        }

        public string KbText => KbArticles.Count == 0 ? string.Empty : string.Join(", ", KbArticles);
    }
}
