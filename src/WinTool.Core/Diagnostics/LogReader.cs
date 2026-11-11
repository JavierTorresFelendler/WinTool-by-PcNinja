using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WinTool.Core.Diagnostics
{
    public sealed class LogEntry
    {
        public LogEntry(string time, string level, string area, string message)
        {
            Time = time;
            Level = level;
            Area = area;
            Message = message;
        }

        public string Time { get; }

        /// <summary>HH:mm:ss; the day is chosen on the page.</summary>
        public string ShortTime => Time.Length >= 19 ? Time.Substring(11) : Time;
        public string Level { get; }
        public string Area { get; }
        public string Message { get; }
        public bool IsWarning => Level == "WARN";
        public bool IsError => Level == "ERROR";
    }

    /// <summary>Reads WinTool's daily log files for the Logs page (read-only, shared with writers).</summary>
    public static class LogReader
    {
        private static readonly Regex Line = new Regex(
            @"^(\d{4}-\d\d-\d\d \d\d:\d\d:\d\d)  (\S+) +(\S+) (.*)$", RegexOptions.CultureInvariant);

        /// <summary>Days that have a log file, newest first.</summary>
        public static IReadOnlyList<DateTime> Days(string directory)
        {
            if (!Directory.Exists(directory)) return new DateTime[0];
            return Security.ProtectedMachineData.PlainFiles(directory).Where(path => Path.GetFileName(path).StartsWith("WinTool-", StringComparison.Ordinal) && path.EndsWith(".log", StringComparison.OrdinalIgnoreCase))
                .Select(path => Path.GetFileNameWithoutExtension(path).Substring("WinTool-".Length))
                .Select(stamp => DateTime.TryParseExact(stamp, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : (DateTime?)null)
                .Where(day => day.HasValue).Select(day => day.Value)
                .OrderByDescending(day => day).ToList();
        }

        public static string PathFor(string directory, DateTime day) =>
            Path.Combine(directory, "WinTool-" + day.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");

        /// <summary>The last <paramref name="maxEntries"/> entries; a missing file gives none.</summary>
        public static IReadOnlyList<LogEntry> ReadTail(string path, int maxEntries)
        {
            if (!File.Exists(path)) return new LogEntry[0];
            var lines = new List<string>();
            // Other WinTool processes (the scheduled task, the command line) may be writing.
            using (var stream = Security.ProtectedMachineData.OpenLog(path))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    lines.Add(line);
                    if (lines.Count > maxEntries * 2) lines.RemoveRange(0, maxEntries);
                }
            }

            return lines.Skip(Math.Max(0, lines.Count - maxEntries)).Select(Parse).ToList();
        }

        public static LogEntry Parse(string line)
        {
            var match = Line.Match(line ?? string.Empty);
            return match.Success
                ? new LogEntry(match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value, match.Groups[4].Value.TrimStart())
                : new LogEntry(string.Empty, string.Empty, string.Empty, line ?? string.Empty);
        }

        public static IEnumerable<string> Directories(string current)
        {
            yield return current;
            if (WinToolPaths.DataRootOverride == null) yield return Path.Combine(WinToolPaths.LegacyMachineData, "Logs");
        }

        public static IReadOnlyList<DateTime> AvailableDays(string current) => Directories(current).SelectMany(directory =>
        {
            try { return Days(directory); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return new DateTime[0]; }
        }).Distinct().OrderByDescending(day => day).ToList();

        public static IReadOnlyList<LogEntry> ReadDay(string current, DateTime day, int count) => ReadDayFrom(Directories(current), day, count);
        public static IReadOnlyList<LogEntry> ReadDayFrom(IEnumerable<string> directories, DateTime day, int count, string legacyDirectory = null) => directories.SelectMany(directory =>
        {
            try
            {
                var entries = ReadTail(PathFor(directory, day), count);
                if (string.Equals(Path.GetFullPath(directory), Path.GetFullPath(legacyDirectory ?? Path.Combine(WinToolPaths.LegacyMachineData, "Logs")), StringComparison.OrdinalIgnoreCase))
                    return entries.Select(entry => new LogEntry(entry.Time, entry.Level, "Legacy / " + entry.Area, entry.Message)).ToList();
                return entries;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return new LogEntry[0]; }
        }).OrderBy(entry => entry.Time, StringComparer.Ordinal).Reverse().Take(count).Reverse().ToList();
    }
}
