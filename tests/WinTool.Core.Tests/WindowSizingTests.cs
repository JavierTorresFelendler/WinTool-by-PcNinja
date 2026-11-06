using WinTool.Core.Platform;
using Xunit;

namespace WinTool.Core.Tests
{
    public class WindowSizingTests
    {
        [Fact]
        public void Large_screen_opens_at_the_designed_size()
        {
            var placement = WindowSizing.Choose(1920, 1040);
            Assert.Equal(1280, placement.Width);
            Assert.Equal(800, placement.Height);
            Assert.False(placement.Maximize);
        }

        [Theory]
        [InlineData(1024, 728)]   // 1024x768, 100%, taskbar 40
        [InlineData(1024, 720)]   // taskbar 48
        [InlineData(1093, 582)]   // 1366x768 at 125%
        [InlineData(800, 560)]    // 1024x768 at 125% (Windows offers it on some drivers)
        public void Small_screens_open_maximized_and_never_below_the_minimum(double width, double height)
        {
            var placement = WindowSizing.Choose(width, height);
            Assert.True(placement.Maximize);
            Assert.True(placement.Width >= WindowSizing.MinimumWidth);
            Assert.True(placement.Height >= WindowSizing.MinimumHeight);
        }

        [Fact]
        public void Medium_screen_fits_with_a_margin()
        {
            var placement = WindowSizing.Choose(1280, 984); // 1280x1024
            Assert.False(placement.Maximize);
            Assert.True(placement.Width <= 1280 - 40);
            Assert.Equal(800, placement.Height);
        }

        [Fact]
        public void The_1024_width_target_uses_the_compact_sidebar()
        {
            Assert.True(WindowSizing.UseCompactSidebar(1024));
            Assert.False(WindowSizing.UseCompactSidebar(1280));
        }

        [Fact]
        public void Minimum_size_fits_1024x768_with_any_taskbar()
        {
            Assert.True(WindowSizing.MinimumWidth <= 1024);
            Assert.True(WindowSizing.MinimumHeight <= 768 - 48);
        }

        [Fact]
        public void Unknown_work_area_uses_the_designed_size()
        {
            var placement = WindowSizing.Choose(0, 0);
            Assert.Equal(1280, placement.Width);
            Assert.False(placement.Maximize);
        }
    }
}
