namespace WinTool.App.ViewModels
{
    public abstract class PageViewModel : LocalizedObject
    {
        protected PageViewModel(string titleKey, string subtitleKey)
        {
            TitleKey = titleKey;
            SubtitleKey = subtitleKey;
        }

        public string TitleKey { get; }
        public string SubtitleKey { get; }
        public string Title => T[TitleKey];
        public string Subtitle => SubtitleKey == null ? string.Empty : T[SubtitleKey];

        /// <summary>
        /// True for a page that fills the window height and scrolls inside itself (Logs);
        /// the window then gives it a fixed viewport instead of scrolling the whole page.
        /// </summary>
        public virtual bool FillsWindow => false;

        /// <summary>Called each time the page is shown.</summary>
        public virtual void OnActivated()
        {
        }

        /// <summary>Called when another page is shown instead.</summary>
        public virtual void OnDeactivated()
        {
        }
    }

    /// <summary>A screen that is planned for a later roadmap phase.</summary>
    public sealed class PlaceholderPageViewModel : PageViewModel
    {
        public PlaceholderPageViewModel(string titleKey, string subtitleKey, int phase)
            : base(titleKey, subtitleKey)
        {
            Phase = phase;
        }

        public int Phase { get; }
        public string Body => T.Format("Placeholder_Body", Phase);
    }
}
