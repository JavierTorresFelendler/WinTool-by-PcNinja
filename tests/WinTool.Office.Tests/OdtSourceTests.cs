using Xunit;

namespace WinTool.Office.Tests;

public class OdtSourceTests
{
    [Fact]
    public void The_newest_link_on_the_page_wins()
    {
        var html = "x https://download.microsoft.com/download/aaaa-1111/officedeploymenttool_18827-20140.exe y " +
                   "\"url\":\"https:\\/\\/download.microsoft.com\\/download\\/bbbb-2222\\/officedeploymenttool_20430-20146.exe\" " +
                   "https://download.microsoft.com/download/cccc/officedeploymenttool_20430-20090.exe";
        Assert.Equal("https://download.microsoft.com/download/bbbb-2222/officedeploymenttool_20430-20146.exe", OdtSource.FindLatest(html));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>no links</html>")]
    [InlineData("http://download.microsoft.com/download/a/officedeploymenttool_1-2.exe")]
    [InlineData("https://download.microsoft.com.evil.test/download/a/officedeploymenttool_1-2.exe")]
    [InlineData("https://evil.test/download/a/officedeploymenttool_1-2.exe")]
    public void Anything_else_is_not_a_link(string html)
    {
        Assert.Null(OdtSource.FindLatest(html));
    }

    [Fact]
    public async Task An_unreadable_page_falls_back_to_the_fixed_address()
    {
        var tools = new FakeTools { PageFails = true };
        var warnings = new List<string>();
        var link = await OdtSource.ResolveAsync(tools, warnings.Add, CancellationToken.None);
        Assert.False(link.Latest);
        Assert.Equal(OdtSource.FallbackUrl, link.Url);
        Assert.Single(warnings);
    }
}
