using System;
using System.IO;
using System.Linq;
using WinTool.Core.Diagnostics;
using Xunit;

namespace WinTool.Core.Tests
{
    public class LegacyLogTests
    {
        [Fact]
        public void Viewer_reads_current_and_legacy_logs_without_moving_them()
        {
            using (var temp = new TempDirectory())
            {
                var current = temp.File("Current"); var legacy = temp.File("Legacy");
                Directory.CreateDirectory(current); Directory.CreateDirectory(legacy);
                var day = new DateTime(2026, 11, 1);
                File.WriteAllText(LogReader.PathFor(current, day), "2026-11-01 11:00:00  INFO   App  current\n");
                var old = LogReader.PathFor(legacy, day); var contents = "2026-11-01 10:00:00  INFO   App  legacy\n"; File.WriteAllText(old, contents);
                var entries = LogReader.ReadDayFrom(new[] { current, legacy }, day, 50);
                Assert.Equal(new[] { "legacy", "current" }, entries.Select(entry => entry.Message));
                Assert.Equal(contents, File.ReadAllText(old));
            }
        }
    }
}
