using WinTool.Core.Platform;
using Xunit;

namespace WinTool.Core.Tests
{
    public class OsInfoTests
    {
        [Theory]
        [InlineData(17763, false)] // Windows 10 1809
        [InlineData(18362, true)]  // Windows 10 1903
        [InlineData(19045, true)]  // Windows 10 22H2
        [InlineData(26100, true)]  // Windows 11 24H2
        public void Minimum_supported_build_is_1903(int build, bool supported)
        {
            Assert.Equal(supported, OsInfo.IsSupportedBuild(build));
        }

        [Fact]
        public void Thirty_two_bit_windows_is_not_supported()
        {
            Assert.False(new OsInfo("Windows 10 Pro", "22H2", 19045, 1, false).IsSupported);
        }

        [Fact]
        public void Windows_11_is_named_from_the_build_number()
        {
            var os = new OsInfo("Windows 10 Pro", "24H2", 26100, 2000, true);
            Assert.Equal("Windows 11 Pro 24H2", os.FriendlyName);
        }

        [Fact]
        public void Windows_10_name_is_kept()
        {
            var os = new OsInfo("Windows 10 Pro", "22H2", 19045, 1, true);
            Assert.Equal("Windows 10 Pro 22H2", os.FriendlyName);
        }
    }
}
