// Static .NET (Core) helpers that extension methods cannot replace; used on every target.
using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WinTool
{
    internal static class Compat
    {
        public static string ToHexString(byte[] bytes)
        {
            var builder = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) builder.Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            return builder.ToString();
        }

        public static void ThrowIfDisposed(bool condition, object instance)
        {
            if (condition) throw new ObjectDisposedException(instance?.GetType().FullName);
        }

        public static void ThrowIfNull(object argument, string name)
        {
            if (argument == null) throw new ArgumentNullException(name);
        }

        public static Task<string> ReadAllTextAsync(string path, CancellationToken token) =>
            Task.Run(() => File.ReadAllText(path, Encoding.UTF8), token);

        public static Task WriteAllTextAsync(string path, string contents, CancellationToken token) =>
            Task.Run(() => File.WriteAllText(path, contents, new UTF8Encoding(false)), token);

        public static long Clamp(long value, long min, long max) => value < min ? min : value > max ? max : value;

        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
    }
}
