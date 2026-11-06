using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Windows;
using WinTool.Core.Localization;

namespace WinTool.App.Localization
{
    /// <summary>
    /// UI text source. XAML binds to the indexer through <see cref="LocExtension"/>,
    /// so switching language updates every open window at once.
    /// </summary>
    public sealed class Loc : INotifyPropertyChanged
    {
        private const string BaseName = "WinTool.App.Localization.Strings";

        private readonly ResourceManager english = new ResourceManager(BaseName, typeof(Loc).Assembly);
        private readonly ResourceManager hebrew = new ResourceManager(BaseName + ".he", typeof(Loc).Assembly);
        private readonly ResourceManager spanish = new ResourceManager(BaseName + ".es", typeof(Loc).Assembly);
        private string language = LanguageResolver.English;

        private Loc()
        {
        }

        public static Loc Instance { get; } = new Loc();

#if DEBUG
        public static bool PseudoAllowed => true;
#else
        public static bool PseudoAllowed => false;
#endif

        public event PropertyChangedEventHandler PropertyChanged;

        public event EventHandler LanguageChanged;

        public string Language => language;

        public FlowDirection FlowDirection => LanguageResolver.IsRightToLeft(language) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        public CultureInfo Culture
        {
            get
            {
                if (language == LanguageResolver.Hebrew) return CultureInfo.GetCultureInfo("he-IL");
                if (language == LanguageResolver.Spanish) return CultureInfo.GetCultureInfo("es-ES");
                return CultureInfo.GetCultureInfo("en-US");
            }
        }

        public string this[string key]
        {
            get
            {
                if (string.IsNullOrEmpty(key)) return string.Empty;
                var englishText = english.GetString(key, CultureInfo.InvariantCulture);
                if (englishText == null) return "!" + key + "!";

                if (language == LanguageResolver.Pseudo) return PseudoLocalizer.Transform(englishText);
                var translated = Manager(language);
                if (translated != null)
                {
                    var text = translated.GetString(key, CultureInfo.InvariantCulture);
                    if (!string.IsNullOrEmpty(text)) return text;
                }

                return englishText;
            }
        }

        public string Format(string key, params object[] args)
        {
            return string.Format(Culture, this[key], args);
        }

        public void SetLanguage(string newLanguage)
        {
            var resolved = string.IsNullOrEmpty(newLanguage) ? LanguageResolver.English : newLanguage;
            if (resolved == language) return;
            language = resolved;
            Raise("Item[]");
            Raise(nameof(Language));
            Raise(nameof(FlowDirection));
            Raise(nameof(Culture));
            LanguageChanged?.Invoke(this, EventArgs.Empty);
        }

        private ResourceManager Manager(string lang) =>
            lang == LanguageResolver.Hebrew ? hebrew : lang == LanguageResolver.Spanish ? spanish : null;

        /// <summary>The keys a language defines, for the self-test and tests.</summary>
        public HashSet<string> KeysFor(string lang)
        {
            var manager = Manager(lang) ?? english;
            var keys = new HashSet<string>(StringComparer.Ordinal);
            // The ResourceManager caches and shares this set; disposing it would break later lookups.
            var set = manager.GetResourceSet(CultureInfo.InvariantCulture, true, false);
            if (set == null) return keys;
            foreach (DictionaryEntry entry in set)
            {
                if (entry.Value is string text && text.Length > 0) keys.Add((string)entry.Key);
            }

            return keys;
        }

        private void Raise(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
