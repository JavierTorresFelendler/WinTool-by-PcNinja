using System;
using System.IO;
using WinTool.Core.IO;
using WinTool.WindowsUpdate.Storage;
using Xunit;

namespace WinTool.WindowsUpdate.Tests
{
    public class LegacyDataValidationTests
    {
        [Fact]
        public void Settings_are_validated_and_historical_success_is_not_promoted_to_evidence()
        {
            var settings = new WindowsUpdateSettings { ScheduleEnabled = true, Frequency = "Weekly", DayOfWeek = "Friday" };
            var migrated = JsonFileStore<WindowsUpdateSettings>.Deserialize(LegacyDataValidation.Settings(JsonFileStore<WindowsUpdateSettings>.Serialize(settings)));
            Assert.True(migrated.ScheduleEnabled); Assert.Equal("Friday", migrated.DayOfWeek);
            settings.SchemaVersion = 99;
            Assert.Throws<FormatException>(() => LegacyDataValidation.Settings(JsonFileStore<WindowsUpdateSettings>.Serialize(settings)));
            var old = new WindowsUpdateState { LastResult = "Succeeded", LastSuccessfulRun = "2026-11-01T12:00:00", LastRunFinished = "2026-11-01T12:00:00", RetryCount = 2 };
            var state = JsonFileStore<WindowsUpdateState>.Deserialize(LegacyDataValidation.State(JsonFileStore<WindowsUpdateState>.Serialize(old)));
            Assert.Equal("LegacyStateUnconfirmed", state.LastResult); Assert.Null(state.LastSuccessfulRun); Assert.Equal(0, state.RetryCount);
        }

        [Fact]
        public void Unavailable_store_returns_read_only_defaults_but_saving_fails()
        {
            var path = Path.Combine(Path.GetTempPath(), "WinTool-store-" + Guid.NewGuid().ToString("N")); File.WriteAllText(path, "fixture");
            try
            {
                var store = new WindowsUpdateStore(path);
                Assert.False(store.LoadSettings().ScheduleEnabled); Assert.Null(store.LoadState().LastResult);
                Assert.Throws<IOException>(() => store.SaveSettings(new WindowsUpdateSettings()));
            }
            finally { File.Delete(path); }
        }
    }
}
