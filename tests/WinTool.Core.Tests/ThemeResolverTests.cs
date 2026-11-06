using WinTool.Core.Platform;
using Xunit;

namespace WinTool.Core.Tests
{
    public class ThemeResolverTests
    {
        [Fact]
        public void System_preference_follows_windows_dark_mode()
        {
            Assert.Equal(AppTheme.Dark, ThemeResolver.Resolve(ThemePreference.System, 0));
            Assert.Equal(AppTheme.Light, ThemeResolver.Resolve(ThemePreference.System, 1));
        }

        [Fact]
        public void Missing_registry_value_means_light()
        {
            Assert.Equal(AppTheme.Light, ThemeResolver.Resolve(ThemePreference.System, null));
            Assert.Equal(AppTheme.Light, ThemeResolver.Resolve(ThemePreference.System, "unexpected"));
        }

        [Fact]
        public void Explicit_preference_overrides_windows()
        {
            Assert.Equal(AppTheme.Light, ThemeResolver.Resolve(ThemePreference.Light, 0));
            Assert.Equal(AppTheme.Dark, ThemeResolver.Resolve(ThemePreference.Dark, 1));
        }
    }
}
