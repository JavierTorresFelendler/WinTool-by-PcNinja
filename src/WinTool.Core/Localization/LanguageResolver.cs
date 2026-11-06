using System;
using System.Collections.Generic;

namespace WinTool.Core.Localization
{
    /// <summary>Picks the UI language (docs/LOCALIZATION.md, "Language selection").</summary>
    public static class LanguageResolver
    {
        public const string Auto = "auto";
        public const string English = "en";
        public const string Hebrew = "he";

        /// <summary>Pseudo-language for layout testing. Offered in debug builds only.</summary>
        public const string Pseudo = "qps";

        public static readonly IReadOnlyList<string> Shipped = new[] { English, Hebrew };

        public static string Resolve(string saved, string installDefault, string windowsUiCulture, bool allowPseudo)
        {
            var fromSaved = Normalize(saved, allowPseudo);
            if (fromSaved != null) return fromSaved;

            var fromInstall = Normalize(installDefault, allowPseudo);
            if (fromInstall != null) return fromInstall;

            var fromWindows = Normalize(TwoLetter(windowsUiCulture), false);
            return fromWindows ?? English;
        }

        public static bool IsRightToLeft(string language)
        {
            return string.Equals(language, Hebrew, StringComparison.OrdinalIgnoreCase);
        }

        private static string Normalize(string value, bool allowPseudo)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var lower = value.Trim().ToLowerInvariant();
            if (lower == Auto) return null;
            if (lower == "iw") lower = Hebrew;
            if (allowPseudo && lower == Pseudo) return Pseudo;
            foreach (var shipped in Shipped)
            {
                if (lower == shipped) return shipped;
            }

            return null;
        }

        private static string TwoLetter(string culture)
        {
            if (string.IsNullOrWhiteSpace(culture)) return null;
            var dash = culture.IndexOf('-');
            return dash > 0 ? culture.Substring(0, dash) : culture;
        }
    }
}
