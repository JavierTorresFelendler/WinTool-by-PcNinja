using WinTool.Core.Localization;
using Xunit;

namespace WinTool.Core.Tests
{
    public class PseudoLocalizerTests
    {
        [Fact]
        public void Wraps_in_brackets_and_makes_text_longer()
        {
            var result = PseudoLocalizer.Transform("Check for Updates");
            Assert.StartsWith("[", result);
            Assert.EndsWith("]", result);
            Assert.True(result.Length >= "Check for Updates".Length * 1.4);
            Assert.DoesNotContain("Check", result);
        }

        [Fact]
        public void Keeps_format_placeholders_working()
        {
            var result = PseudoLocalizer.Transform("Version {0}");
            Assert.Contains("{0}", result);
            Assert.Contains("1.2.3", string.Format(result, "1.2.3"));
        }

        [Fact]
        public void Empty_text_stays_empty()
        {
            Assert.Equal(string.Empty, PseudoLocalizer.Transform(string.Empty));
        }
    }
}
