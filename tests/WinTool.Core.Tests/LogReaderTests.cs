using System;
using System.IO;
using System.Linq;
using WinTool.Core.Diagnostics;
using Xunit;

namespace WinTool.Core.Tests
{
    public class LogReaderTests : IDisposable
    {
        private readonly string folder = Path.Combine(Path.GetTempPath(), "WinToolLogReader", Guid.NewGuid().ToString("N"));

        public LogReaderTests() => Directory.CreateDirectory(folder);

        public void Dispose()
        {
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }

        [Fact]
        public void Lines_written_by_AppLog_parse_back()
        {
            var line = AppLog.FormatLine(new DateTime(2026, 11, 6, 10, 30, 2), "WARN", "WindowsUpdate", "Reboot pending");
            var entry = LogReader.Parse(line);
            Assert.Equal("2026-11-06 10:30:02", entry.Time);
            Assert.Equal("WARN", entry.Level);
            Assert.Equal("WindowsUpdate", entry.Area);
            Assert.Equal("Reboot pending", entry.Message);
            Assert.True(entry.IsWarning);

            var shortArea = LogReader.Parse(AppLog.FormatLine(DateTime.Now, "INFO", "UI", "  indented text"));
            Assert.Equal("UI", shortArea.Area);
            Assert.Equal("indented text", shortArea.Message);
        }

        [Fact]
        public void A_foreign_line_is_kept_as_message()
        {
            var entry = LogReader.Parse("not a log line");
            Assert.Equal("not a log line", entry.Message);
            Assert.Equal(string.Empty, entry.Area);
        }

        [Fact]
        public void Days_are_newest_first_and_ignore_other_files()
        {
            foreach (var name in new[] { "WinTool-20261104.log", "WinTool-20261106.log", "WinTool-bad.log", "other.txt" })
                File.WriteAllText(Path.Combine(folder, name), "x");
            Assert.Equal(new[] { new DateTime(2026, 11, 6), new DateTime(2026, 11, 4) }, LogReader.Days(folder));
            Assert.Empty(LogReader.Days(Path.Combine(folder, "missing")));
        }

        [Fact]
        public void The_tail_keeps_the_last_entries_while_the_file_is_open_for_writing()
        {
            var path = LogReader.PathFor(folder, new DateTime(2026, 11, 6));
            File.WriteAllLines(path, Enumerable.Range(1, 250).Select(i => AppLog.FormatLine(DateTime.Now, "INFO", "Office", "line " + i)));
            using (new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            {
                var tail = LogReader.ReadTail(path, 100);
                Assert.Equal(100, tail.Count);
                Assert.Equal("line 151", tail[0].Message);
                Assert.Equal("line 250", tail[99].Message);
            }

            Assert.Empty(LogReader.ReadTail(Path.Combine(folder, "none.log"), 10));
        }
    }
}
