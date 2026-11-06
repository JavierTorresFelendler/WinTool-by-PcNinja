using System;
using Xunit;

namespace WinTool.Core.Tests
{
    public class ReleaseFilesTests
    {
        [Fact]
        public void Official_names_have_no_version()
        {
            Assert.Equal("WinTool-by-PcNinja.exe", ReleaseFiles.Executable);
            Assert.Equal("WinTool-by-PcNinja.msi", ReleaseFiles.Installer);
        }

        [Theory]
        [InlineData("1.2.0.0", "WinTool-by-PcNinja-v1.2.0.0.exe")]
        [InlineData("1.2", "WinTool-by-PcNinja-v1.2.0.0.exe")]
        [InlineData("0.1.0", "WinTool-by-PcNinja-v0.1.0.0.exe")]
        public void Previous_copy_gets_the_full_four_part_version(string version, string expected)
        {
            Assert.Equal(expected, ReleaseFiles.PreviousCopyName(new Version(version)));
        }
    }
}
