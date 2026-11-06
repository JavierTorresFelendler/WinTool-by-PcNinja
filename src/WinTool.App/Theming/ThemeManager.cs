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

            var dictionary = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/WinTool;component/Themes/" + theme + ".xaml", UriKind.Absolute)
            };

            var merged = application.Resources.MergedDictionaries;
            if (active != null) merged.Remove(active);
            merged.Insert(0, dictionary);
            active = dictionary;
            Current = theme;
            ThemeChanged?.Invoke(null, EventArgs.Empty);
        }

        public static IEnumerable<string> ThemeResourceNames()
        {
            foreach (VisualTheme theme in Enum.GetValues(typeof(VisualTheme)))
            {
                yield return "themes/" + theme.ToString().ToLowerInvariant() + ".baml";
            }

            yield return "themes/styles.baml";
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
