using System.IO;
using WinTool.Core.Platform;
using WinTool.Core.Settings;
using Xunit;

namespace WinTool.Core.Tests
{
    public class SettingsStoreTests
    {
        [Fact]
        public void Missing_file_gives_defaults()
        {
            using (var temp = new TempDirectory())
            {
                var settings = new SettingsStore(temp.File("settings.json")).Load();
                Assert.Equal(ThemePreference.System, settings.ThemePreference);
                Assert.Equal("auto", settings.Language);
                Assert.True(settings.CheckForAppUpdatesAtStartup);
            }
        }

        [Fact]
        public void Round_trips_all_values()
        {
            using (var temp = new TempDirectory())
            {
                var store = new SettingsStore(temp.File("settings.json"));
                store.Save(new AppSettings { ThemePreference = ThemePreference.Dark, Language = "he", CheckForAppUpdatesAtStartup = false });
                var loaded = store.Load();
                Assert.Equal(ThemePreference.Dark, loaded.ThemePreference);
                Assert.Equal("he", loaded.Language);
                Assert.False(loaded.CheckForAppUpdatesAtStartup);
            }
        }

        [Fact]
        public void Damaged_file_gives_defaults_instead_of_failing()
        {
            using (var temp = new TempDirectory())
            {
                var path = temp.File("settings.json");
                File.WriteAllText(path, "{ this is not json");
                var settings = new SettingsStore(path).Load();
                Assert.Equal(ThemePreference.System, settings.ThemePreference);
            }
        }

        [Fact]
        public void Unknown_theme_value_falls_back_to_system()
        {
            using (var temp = new TempDirectory())
            {
                var path = temp.File("settings.json");
                File.WriteAllText(path, "{\"schemaVersion\":1,\"theme\":\"Purple\",\"language\":\"en\",\"checkForAppUpdatesAtStartup\":true}");
                Assert.Equal(ThemePreference.System, new SettingsStore(path).Load().ThemePreference);
            }
        }
    }
}
