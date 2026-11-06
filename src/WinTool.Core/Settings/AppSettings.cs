using System.Runtime.Serialization;
using WinTool.Core.Platform;

namespace WinTool.Core.Settings
{
    /// <summary>Per-user preferences. Unknown or missing values fall back to defaults.</summary>
    [DataContract]
    public sealed class AppSettings
    {
        public const int CurrentSchemaVersion = 1;

        public AppSettings()
        {
            SchemaVersion = CurrentSchemaVersion;
            Theme = ThemePreference.System.ToString();
            Language = Localization.LanguageResolver.Auto;
            CheckForAppUpdatesAtStartup = true;
        }

        [DataMember(Name = "schemaVersion", Order = 0)]
        public int SchemaVersion { get; set; }

        /// <summary>System, Light or Dark.</summary>
        [DataMember(Name = "theme", Order = 1)]
        public string Theme { get; set; }

        /// <summary>auto, en, he (and qps in debug builds).</summary>
        [DataMember(Name = "language", Order = 2)]
        public string Language { get; set; }

        [DataMember(Name = "checkForAppUpdatesAtStartup", Order = 3)]
        public bool CheckForAppUpdatesAtStartup { get; set; }

        public ThemePreference ThemePreference
        {
            get
            {
                ThemePreference value;
                return System.Enum.TryParse(Theme, true, out value) ? value : ThemePreference.System;
            }
            set { Theme = value.ToString(); }
        }
    }
}
