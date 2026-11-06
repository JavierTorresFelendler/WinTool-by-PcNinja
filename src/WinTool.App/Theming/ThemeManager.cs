using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Resources;
using System.Windows;
using WinTool.Core.Platform;

namespace WinTool.App.Theming
{
    public enum VisualTheme
    {
        Dark,
        Light,
        HighContrast
    }

    /// <summary>
    /// Swaps the color dictionary at the front of the application resources.
    /// Every brush in the UI is a DynamicResource, so open windows recolor at once.
    /// </summary>
    public static class ThemeManager
    {
        private static ResourceDictionary active;

        public static VisualTheme Current { get; private set; }

        public static event EventHandler ThemeChanged;

        public static VisualTheme Choose(ThemePreference preference, object appsUseLightTheme, bool highContrast)
        {
            if (highContrast) return VisualTheme.HighContrast;
            return ThemeResolver.Resolve(preference, appsUseLightTheme) == AppTheme.Light ? VisualTheme.Light : VisualTheme.Dark;
        }

        public static void Apply(VisualTheme theme)
        {
            var application = Application.Current;
            if (application == null) return;
            if (active != null && Current == theme) return;

            var dictionary = theme == VisualTheme.HighContrast
                ? CreateHighContrastDictionary()
                : new ResourceDictionary { Source = new Uri("pack://application:,,,/WinTool;component/Themes/" + theme + ".xaml", UriKind.Absolute) };

            var merged = application.Resources.MergedDictionaries;
            if (active != null) merged.Remove(active);
            merged.Insert(0, dictionary);
            active = dictionary;
            Current = theme;
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        public static IEnumerable<string> ThemeResourceNames()
        {
            yield return "themes/dark.baml";
            yield return "themes/light.baml";
            yield return "themes/styles.baml";
        }

        /// <summary>
        /// Windows High Contrast: every brush comes from the current system colors
        /// (decision D-012). Built in code because the keys map to system brushes.
        /// </summary>
        private static ResourceDictionary CreateHighContrastDictionary()
        {
            var map = new Dictionary<string, System.Windows.Media.Brush>
            {
                { "Brush.Bg", SystemColors.WindowBrush },
                { "Brush.Surface", SystemColors.WindowBrush },
                { "Brush.Side", SystemColors.ControlBrush },
                { "Brush.Raised", SystemColors.ControlBrush },
                { "Brush.Input", SystemColors.WindowBrush },
                { "Brush.Text", SystemColors.WindowTextBrush },
                { "Brush.Muted", SystemColors.WindowTextBrush },
                { "Brush.Divider", SystemColors.WindowTextBrush },
                { "Brush.Border", SystemColors.WindowTextBrush },
                { "Brush.CardBorder", SystemColors.WindowTextBrush },
                { "Brush.Hover", SystemColors.HighlightBrush },
                { "Brush.Accent", SystemColors.HighlightBrush },
                { "Brush.OnAccent", SystemColors.HighlightTextBrush },
                { "Brush.AccentText", SystemColors.HotTrackBrush },
                { "Brush.AccentSoft", SystemColors.ControlBrush },
                { "Brush.Ok", SystemColors.WindowTextBrush },
                { "Brush.Warn", SystemColors.WindowTextBrush },
                { "Brush.WarnText", SystemColors.WindowTextBrush },
                { "Brush.WarnSoft", SystemColors.ControlBrush },
                { "Brush.Danger", SystemColors.WindowTextBrush },
                { "Brush.DangerBorder", SystemColors.WindowTextBrush },
                { "Brush.CaptionCloseHover", SystemColors.HighlightBrush },
                { "Brush.CaptionCloseText", SystemColors.HighlightTextBrush },
            };

            var dictionary = new ResourceDictionary();
            foreach (var pair in map) dictionary[pair.Key] = pair.Value;
            return dictionary;
        }

        /// <summary>Names of the compiled XAML resources inside WinTool.exe.</summary>
        public static HashSet<string> CompiledResourceKeys()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var manager = new ResourceManager("WinTool.g", typeof(ThemeManager).Assembly);
            using (var set = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false))
            {
                if (set == null) return keys;
                foreach (DictionaryEntry entry in set) keys.Add((string)entry.Key);
            }

            return keys;
        }
    }
}
