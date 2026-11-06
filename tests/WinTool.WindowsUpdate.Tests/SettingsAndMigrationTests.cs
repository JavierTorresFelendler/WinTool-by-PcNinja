using WinTool.Core.IO;
using WinTool.WindowsUpdate.Migration;
using WinTool.WindowsUpdate.Storage;
using Xunit;

namespace WinTool.WindowsUpdate.Tests
{
    public class SettingsTests
    {
        [Fact]
        public void Defaults_match_the_msi_defaults_and_are_valid()
        {
            var settings = new WindowsUpdateSettings();
            Assert.False(settings.ScheduleEnabled);
            Assert.Equal("Monthly", settings.Frequency);
            Assert.Equal(15, settings.MonthlyDay);
            Assert.Equal("03:00", settings.Time);
            Assert.Equal(5, settings.StartupDelayMinutes);
            Assert.True(settings.RunIfMissed);
            Assert.False(settings.InstallFirmwareUpdates);
            Assert.Empty(settings.Validate());
        }

        [Theory]
        [InlineData("Hourly", "03:00", 15, "Frequency")]
        [InlineData("Daily", "25:00", 15, "Time")]
        [InlineData("Daily", "3pm", 15, "Time")]
        [InlineData("Monthly", "03:00", 29, "MonthlyDay")]
        [InlineData("Monthly", "03:00", 0, "MonthlyDay")]
        public void Invalid_values_are_reported_not_fixed(string frequency, string time, int day, string field)
        {
            var settings = new WindowsUpdateSettings { Frequency = frequency, Time = time, MonthlyDay = day };
            Assert.Contains(settings.Validate(), e => e.StartsWith(field));
        }

        [Fact]
        public void Weekly_needs_a_day_name()
        {
            Assert.Contains(new WindowsUpdateSettings { Frequency = "Weekly", DayOfWeek = "3" }.Validate(), e => e.StartsWith("DayOfWeek"));
            Assert.Empty(new WindowsUpdateSettings { Frequency = "Weekly", DayOfWeek = "Friday" }.Validate());
        }

        [Fact]
        public void Settings_round_trip_as_json()
        {
            var original = new WindowsUpdateSettings { ScheduleEnabled = true, Frequency = "Weekly", DayOfWeek = "Friday", WakeToRun = true };
            var copy = JsonFileStore<WindowsUpdateSettings>.Deserialize(JsonFileStore<WindowsUpdateSettings>.Serialize(original));
            Assert.True(copy.ScheduleEnabled);
            Assert.Equal("Friday", copy.DayOfWeek);
            Assert.True(copy.WakeToRun);
        }
    }

    public class LegacyImportTests
    {
        // Shape of a WinUpdate Tool V2.2.8 config.json (PowerShell ConvertTo-Json output).
        private const string V2Config = @"{
    ""ConfigVersion"":  11,
    ""Enabled"":  true,
    ""Frequency"":  ""Weekly"",
    ""Time"":  ""02:30"",
    ""DayOfWeek"":  ""Saturday"",
    ""MonthlyDay"":  15,
    ""RunAtStartup"":  true,
    ""StartupDelayMinutes"":  15,
    ""RunIfMissed"":  false,
    ""WakeToRun"":  true,
    ""ShowRebootPrompt"":  true,
    ""InstallFirmwareUpdates"":  false,
    ""AutoRetryEnabled"":  true,
    ""RetryInitialDelayMinutes"":  5,
    ""RetryMaxAttempts"":  4,
    ""RetryBackoffMultiplier"":  2,
    ""MinimumCooldownMinutes"":  5,
    ""DisplayTheme"":  ""Dark"",
    ""LastSaved"":  ""2026-10-31T12:00:00""
}";

        [Fact]
        public void Imports_every_schedule_setting_from_v2()
        {
            var settings = LegacyWinUpdateConfig.Import(V2Config);
            Assert.True(settings.ScheduleEnabled);
            Assert.Equal("Weekly", settings.Frequency);
            Assert.Equal("02:30", settings.Time);
            Assert.Equal("Saturday", settings.DayOfWeek);
            Assert.True(settings.RunAtStartup);
            Assert.Equal(15, settings.StartupDelayMinutes);
            Assert.False(settings.RunIfMissed);
            Assert.True(settings.WakeToRun);
            Assert.Equal(4, settings.RetryMaxAttempts);
            Assert.Empty(settings.Validate());
        }

        [Fact]
        public void Bad_or_missing_values_fall_back_to_defaults()
        {
            var settings = LegacyWinUpdateConfig.Import(@"{ ""Enabled"": true, ""Frequency"": ""Hourly"", ""Time"": ""late"", ""MonthlyDay"": 31 }");
            Assert.True(settings.ScheduleEnabled);
            Assert.Equal("Monthly", settings.Frequency);
            Assert.Equal("03:00", settings.Time);
            Assert.Equal(28, settings.MonthlyDay);
            Assert.Empty(settings.Validate());
        }

        [Fact]
        public void Empty_input_gives_defaults()
        {
            Assert.False(LegacyWinUpdateConfig.Import(string.Empty).ScheduleEnabled);
        }
    }
}
