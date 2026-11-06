// .NET (Core) APIs used by the ported Windows Upgrade code, implemented for
// .NET Framework 4.8 as extension methods. On .NET 8 the built-in members win.
#if NETFRAMEWORK
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace WinTool
{
    internal static class FrameworkExtensions
    {
        public static bool Contains(this string text, string value, StringComparison comparison) =>
            text.IndexOf(value, comparison) >= 0;

        public static bool Contains(this string text, char value) => text.IndexOf(value) >= 0;

        public static bool StartsWith(this string text, char value) => text.Length > 0 && text[0] == value;

        public static bool EndsWith(this string text, char value) => text.Length > 0 && text[text.Length - 1] == value;

        public static string Replace(this string text, string oldValue, string newValue, StringComparison comparison)
        {
            if (string.IsNullOrEmpty(oldValue)) return text;
            var builder = new System.Text.StringBuilder();
            var position = 0;
            int index;
            while ((index = text.IndexOf(oldValue, position, comparison)) >= 0)
            {
                builder.Append(text, position, index - position).Append(newValue);
                position = index + oldValue.Length;
            }

            return builder.Append(text, position, text.Length - position).ToString();
        }

        public static string[] Split(this string text, char separator, StringSplitOptions options) =>
            text.Split(new[] { separator }, options);

        public static string[] Split(this string text, string separator, StringSplitOptions options) =>
            text.Split(new[] { separator }, options);

        public static Task<int> ReadAsync(this Stream stream, byte[] buffer, CancellationToken token) =>
            stream.ReadAsync(buffer, 0, buffer.Length, token);

        public static Task WriteAsync(this Stream stream, byte[] buffer, CancellationToken token) =>
            stream.WriteAsync(buffer, 0, buffer.Length, token);

        public static async Task<string> ReadToEndAsync(this TextReader reader, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var read = reader.ReadToEndAsync();
            var completed = await Task.WhenAny(read, Task.Delay(Timeout.Infinite, token)).ConfigureAwait(false);
            if (completed != read) token.ThrowIfCancellationRequested();
            return await read.ConfigureAwait(false);
        }

        public static Task WaitForExitAsync(this Process process, CancellationToken token)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            process.EnableRaisingEvents = true;
            process.Exited += (sender, e) => completion.TrySetResult(true);
            if (process.HasExited) completion.TrySetResult(true);
            if (token.CanBeCanceled) token.Register(() => completion.TrySetCanceled(token));
            return completion.Task;
        }

        /// <summary>Process.Kill(entireProcessTree) for .NET Framework: taskkill /T, then Kill as a fallback.</summary>
        public static void Kill(this Process process, bool entireProcessTree)
        {
            if (!entireProcessTree)
            {
                process.Kill();
                return;
            }

            try
            {
                var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
                using (var taskkill = Process.Start(new ProcessStartInfo(Path.Combine(system, "taskkill.exe"), "/PID " + process.Id + " /T /F")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                }))
                {
                    taskkill?.WaitForExit(15000);
                }
            }
            finally
            {
                if (!process.HasExited) process.Kill();
            }
        }

        public static bool TryAdd<TKey, TValue>(this System.Collections.Generic.Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
        {
            if (dictionary.ContainsKey(key)) return false;
            dictionary.Add(key, value);
            return true;
        }
    }
}
#endif
