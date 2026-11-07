using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using WinTool.Core.Diagnostics;
using WinTool.Core.IO;

namespace WinTool.Core.Settings
{
    /// <summary>Loads and saves <see cref="AppSettings"/> as JSON, written atomically.</summary>
    public sealed class SettingsStore
    {
        public SettingsStore(string path)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public string Path { get; }

        public static string DefaultPath
        {
            get
            {
                return System.IO.Path.Combine(WinToolPaths.UserData, "settings.json");
            }
        }

        /// <summary>Returns defaults when the file is missing or unreadable; a damaged file never stops the app.</summary>
        public AppSettings Load()
        {
            try
            {
                var text = AtomicFile.ReadAllTextOrNull(Path);
                if (string.IsNullOrWhiteSpace(text)) return new AppSettings();
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(text)))
                {
                    var loaded = (AppSettings)CreateSerializer().ReadObject(stream);
                    return Normalize(loaded);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is SerializationException || ex is UnauthorizedAccessException || ex is InvalidCastException)
            {
                AppLog.Current.Warn("Settings", "Settings could not be read; defaults are used. " + ex.Message);
                return new AppSettings();
            }
        }

        public void Save(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            using (var stream = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false, true, "  "))
                {
                    CreateSerializer().WriteObject(writer, settings);
                }

                AtomicFile.WriteAllBytes(Path, stream.ToArray());
            }
        }

        private static DataContractJsonSerializer CreateSerializer()
        {
            return new DataContractJsonSerializer(typeof(AppSettings));
        }

        private static AppSettings Normalize(AppSettings settings)
        {
            if (settings == null) return new AppSettings();
            var defaults = new AppSettings();
            if (string.IsNullOrWhiteSpace(settings.Theme)) settings.Theme = defaults.Theme;
            if (string.IsNullOrWhiteSpace(settings.Language)) settings.Language = defaults.Language;
            if (settings.SchemaVersion <= 0) settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
            return settings;
        }
    }
}
