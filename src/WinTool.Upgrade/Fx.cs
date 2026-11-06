using System.Diagnostics;
using System.Text.RegularExpressions;

namespace WinTool.Upgrade;

/// <summary>
/// Framework calls that exist on .NET 8 but not on .NET Framework 4.8. On .NET 8
/// each forwards to the real API so the tests check the same behavior the port keeps.
/// </summary>
internal static class Fx
{
    public static bool IsWindows =>
#if NETFRAMEWORK
        Environment.OSVersion.Platform == PlatformID.Win32NT;
#else
        OperatingSystem.IsWindows();
#endif

    public static long TickCount64 =>
#if NETFRAMEWORK
        Stopwatch.GetTimestamp() / (Stopwatch.Frequency / 1000);
#else
        Environment.TickCount64;
#endif

    public static bool IsAsciiLetter(char c) => (uint)((c | 0x20) - 'a') <= 'z' - 'a';

    /// <summary>Path.TrimEndingDirectorySeparator: drops one trailing separator unless the path is a root.</summary>
    public static string TrimEndingDirectorySeparator(string path)
    {
#if NETFRAMEWORK
        if (string.IsNullOrEmpty(path)) return path;
        var last = path[path.Length - 1];
        if (last != Path.DirectorySeparatorChar && last != Path.AltDirectorySeparatorChar) return path;
        var root = Path.GetPathRoot(path);
        return root != null && path.Length <= root.Length ? path : path.Substring(0, path.Length - 1);
#else
        return Path.TrimEndingDirectorySeparator(path);
#endif
    }

    /// <summary>Path.IsPathFullyQualified on Windows: "C:\x", "\\server\share" or a device path.</summary>
    public static bool IsPathFullyQualified(string path)
    {
#if NETFRAMEWORK
        if (path == null) throw new ArgumentNullException(nameof(path));
        static bool Separator(char c) => c == '\\' || c == '/';
        if (path.Length < 2) return false;
        if (Separator(path[0])) return path[1] == '?' || Separator(path[1]);
        return path.Length >= 3 && path[1] == ':' && Separator(path[2]) && IsAsciiLetter(path[0]);
#else
        return Path.IsPathFullyQualified(path);
#endif
    }

    public static Guid GuidAt(byte[] buffer, int offset)
    {
        var bytes = new byte[16];
        Array.Copy(buffer, offset, bytes, 0, 16);
        return new Guid(bytes);
    }

    /// <summary>Stream.ReadAtLeast(buffer, minimum, throwOnEndOfStream: false).</summary>
    public static int ReadAtLeast(Stream stream, byte[] buffer, int minimum)
    {
        var total = 0;
        while (total < minimum)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read == 0) break;
            total += read;
        }
        return total;
    }

    public static RegexOptions NonBacktracking =>
#if NETFRAMEWORK
        RegexOptions.None; // Backtracking engine; the 1 second match timeout still bounds it.
#else
        RegexOptions.NonBacktracking;
#endif
}
