using System;
using System.IO;
using WinTool.Core.Diagnostics;
using Xunit;

namespace WinTool.Core.Tests
{
    public class AppLogTests
    {
        [Fact]
        public void Writes_one_line_per_entry_into_a_daily_file()
        {
            using (var temp = new TempDirectory())
            {
                var log = new AppLog(temp.Path, () => new DateTime(2026, 11, 6, 10, 30, 2));
                log.Info("WindowsUpdate", "Scan started\r\n(Manual)");
                log.Warn("Health", "Reboot pending");
                var lines = File.ReadAllLines(Path.Combine(temp.Path, "WinTool-20261106.log"));
                Assert.Equal(2, lines.Length);
                Assert.Equal("2026-11-06 10:30:02  INFO   WindowsUpdate  Scan started  (Manual)", lines[0]);
                Assert.Contains("WARN", lines[1]);
            }
        }

        [Fact]
        public void Hebrew_text_survives_round_trip()
        {
            using (var temp = new TempDirectory())
            {
                var log = new AppLog(temp.Path);
                log.Info("WindowsUpdate", "עדכון מצטבר עבור Windows");
                Assert.Contains("עדכון מצטבר עבור Windows", File.ReadAllText(log.CurrentFilePath));
            }
        }

        [Fact]
        public void Unwritable_location_does_not_throw()
        {
            var log = new AppLog("\0invalid");
            log.Error("Test", "Must not throw", new InvalidOperationException("x"));
        }
    }
}
