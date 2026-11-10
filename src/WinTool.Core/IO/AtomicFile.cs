using System;
using System.IO;
using System.Text;

namespace WinTool.Core.IO
{
    /// <summary>
    /// Writes a file so that readers see either the old or the new content,
    /// never a partial file (lesson from WinUpdate Tool V2.2.6).
    /// </summary>
    public static class AtomicFile
    {
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        public static void WriteAllText(string path, string contents)
        {
            WriteAllBytes(path, Utf8NoBom.GetBytes(contents ?? string.Empty));
        }

        public static void WriteAllBytes(string path, byte[] bytes)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("A path is required.", nameof(path));
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            WinToolPaths.EnsureMachineDataDirectory(directory);
            var temporary = Path.Combine(directory, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");

            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(fullPath))
                {
                    File.Replace(temporary, fullPath, null, true);
                }
                else
                {
                    File.Move(temporary, fullPath);
                }
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
        }

        public static string ReadAllTextOrNull(string path)
        {
            return File.Exists(path) ? File.ReadAllText(path, Utf8NoBom) : null;
        }
    }
}
