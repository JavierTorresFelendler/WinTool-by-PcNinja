using WinTool.Core.Localization;
using Xunit;

namespace WinTool.Core.Tests
{
    public class LanguageResolverTests
    {
        [Theory]
        [InlineData("he", null, "en-US", "he")]          // saved choice wins
        [InlineData("auto", "he", "en-US", "he")]        // then the MSI default
        [InlineData(null, "auto", "he-IL", "he")]        // then the Windows language
        [InlineData(null, null, "fr-FR", "en")]          // unsupported falls back to English
        [InlineData("es", null, "en-US", "en")]          // Spanish is not shipped yet
        [InlineData("iw", null, "en-US", "he")]          // legacy Hebrew code
        [InlineData(" HE ", null, "en-US", "he")]
        public void Resolves_in_documented_order(string saved, string install, string windows, string expected)
        {
            Assert.Equal(expected, LanguageResolver.Resolve(saved, install, windows, false));
        }

        [Fact]
        public void Pseudo_language_only_when_allowed()
        {
            Assert.Equal("qps", LanguageResolver.Resolve("qps", null, "en-US", true));
            Assert.Equal("en", LanguageResolver.Resolve("qps", null, "en-US", false));
        }

        [Fact]
        public void Hebrew_is_right_to_left()
        {
            Assert.True(LanguageResolver.IsRightToLeft("he"));
            Assert.False(LanguageResolver.IsRightToLeft("en"));
        }
    }
}
