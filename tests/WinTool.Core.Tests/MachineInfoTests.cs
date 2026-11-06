using WinTool.Core.Machine;
using Xunit;

namespace WinTool.Core.Tests
{
    public class MachineInfoTests
    {
        [Theory]
        [InlineData(19045, true)]
        [InlineData(22631, false)]
        [InlineData(0, false)]
        public void Windows_10_is_recognised_by_build(int build, bool expected)
        {
            Assert.Equal(expected, MachineInfo.IsWindows10(build));
        }

        [Fact]
        public void Bytes_are_shown_in_gigabytes()
        {
            Assert.Equal("16 GB", MachineInfo.FormatBytes(16L * 1024 * 1024 * 1024));
            Assert.Equal("112 GB", MachineInfo.FormatBytes(112L * 1024 * 1024 * 1024));
            Assert.Equal(string.Empty, MachineInfo.FormatBytes(null));
        }

        [Theory]
        [InlineData("http://officecdn.microsoft.com/pr/492350f6-3a01-4f97-b9c0-c7c6ddf67d60", "Current Channel")]
        [InlineData("http://officecdn.microsoft.com/pr/55336b82-a18d-4dd6-b5f6-9e5095c314a6/", "Monthly Enterprise Channel")]
        [InlineData("http://officecdn.microsoft.com/pr/unknown", null)]
        public void Office_channels_are_named(string url, string expected)
        {
            Assert.Equal(expected, OfficeInfo.ChannelName(url));
        }

        [Fact]
        public void Office_products_are_named_and_2019_is_out_of_support()
        {
            Assert.Equal("Office 2019 Professional Plus", OfficeInfo.ProductNameFor("ProPlus2019Volume,VisioPro2019Volume"));
            Assert.Equal("Microsoft 365 Apps for Enterprise", OfficeInfo.ProductNameFor("O365ProPlusRetail"));
            Assert.Equal("SomethingNew2030", OfficeInfo.ProductNameFor("SomethingNew2030"));
            Assert.True(OfficeInfo.IsOutOfSupport("ProPlus2019Volume"));
            Assert.False(OfficeInfo.IsOutOfSupport("O365ProPlusRetail"));
        }
    }
}
