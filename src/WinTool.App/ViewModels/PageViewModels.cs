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

        /// <summary>Called each time the page is shown.</summary>
        public virtual void OnActivated()
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
