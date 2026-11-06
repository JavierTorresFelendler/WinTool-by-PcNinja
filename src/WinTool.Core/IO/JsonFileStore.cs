using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using WinTool.Core.Diagnostics;

namespace WinTool.Core.IO
{
    /// <summary>
    /// A JSON file for one data-contract type, written atomically. A missing
    /// or damaged file yields null, never an exception, so bad data cannot
    /// stop the app.
    /// </summary>
    public sealed class JsonFileStore<T> where T : class
    {
        public JsonFileStore(string path)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public string Path { get; }

        public T LoadOrNull()
        {
            try
            {
                var text = AtomicFile.ReadAllTextOrNull(Path);
                if (string.IsNullOrWhiteSpace(text)) return null;
                return Deserialize(text);
            }
            catch (Exception ex) when (ex is IOException || ex is SerializationException || ex is UnauthorizedAccessException || ex is InvalidCastException)
            {
                AppLog.Current.Warn("Storage", "Could not read " + Path + "; defaults are used. " + ex.Message);
                return null;
            }
        }

        public void Save(T value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            AtomicFile.WriteAllText(Path, Serialize(value));
        }

        public static string Serialize(T value)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, Encoding.UTF8, false, true, "  "))
                {
                    new DataContractJsonSerializer(typeof(T)).WriteObject(writer, value);
                }

                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static T Deserialize(string json)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
            }
        }
    }
}
