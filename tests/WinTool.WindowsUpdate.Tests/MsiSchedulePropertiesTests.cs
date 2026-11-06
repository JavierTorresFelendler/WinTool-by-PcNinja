using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using WinTool.WindowsUpdate.Schedule;
using WinTool.WindowsUpdate.Storage;
using Xunit;

namespace WinTool.WindowsUpdate.Tests
{
    public class MsiSchedulePropertiesTests
    {
        [Fact]
        public void Every_property_exists_in_the_msi_and_the_schedule_is_enabled()
        {
            var properties = MsiScheduleProperties.From(new WindowsUpdateSettings());
            var wxs = File.ReadAllText(FindRepoFile("packaging/msi/Package.wxs"));
            foreach (var property in properties)
                Assert.True(Regex.IsMatch(wxs, "<Property Id=\"" + property.Key + "\""), property.Key + " is not an MSI property");
            Assert.Equal("1", properties.Single(p => p.Key == "WINTOOL_ENABLE_SCHEDULE").Value);
        }

        [Fact]
        public void Values_follow_the_settings_and_booleans_are_one_or_empty()
        {
            var settings = new WindowsUpdateSettings
            {
                Frequency = "Weekly", Time = "03:30", DayOfWeek = "Sunday", MonthlyDay = 9,
                RunAtStartup = false, WakeToRun = true, InstallFirmwareUpdates = false, RetryMaxAttempts = 4
            };
            var line = MsiScheduleProperties.CommandLine(settings);
            Assert.Contains("WINTOOL_FREQUENCY=\"Weekly\"", line);
            Assert.Contains("WINTOOL_TIME=\"03:30\"", line);
            Assert.Contains("WINTOOL_MONTHLY_DAY=\"9\"", line);
            Assert.Contains("WINTOOL_WAKE_TO_RUN=\"1\"", line);
            Assert.Contains("WINTOOL_RUN_AT_STARTUP=\"\"", line);
            Assert.Contains("WINTOOL_ALLOW_FIRMWARE=\"\"", line);
            Assert.Contains("WINTOOL_RETRY_MAX_ATTEMPTS=\"4\"", line);
        }

        private static string FindRepoFile(string relative)
        {
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, relative))) dir = dir.Parent;
            Assert.NotNull(dir);
            return Path.Combine(dir.FullName, relative);
        }
    }
}
