using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace WinTool.Core.Tests
{
    /// <summary>Every English string has a Hebrew translation with the same placeholders (docs/LOCALIZATION.md).</summary>
    public class ResourceParityTests
    {
        private static readonly Regex Placeholder = new Regex(@"\{\d+\}", RegexOptions.Compiled);

        [Fact]
        public void Hebrew_has_every_english_key_with_the_same_placeholders()
        {
            var english = Load("Strings.resx");
            var hebrew = Load("Strings.he.resx");

            var missing = english.Keys.Except(hebrew.Keys).ToList();
            var extra = hebrew.Keys.Except(english.Keys).ToList();
            Assert.True(missing.Count == 0, "Missing in Hebrew: " + string.Join(", ", missing));
            Assert.True(extra.Count == 0, "Only in Hebrew: " + string.Join(", ", extra));

            foreach (var key in english.Keys)
            {
                Assert.False(string.IsNullOrWhiteSpace(english[key]), "Empty English text: " + key);
                Assert.False(string.IsNullOrWhiteSpace(hebrew[key]), "Empty Hebrew text: " + key);
                var expected = Placeholder.Matches(english[key]).Cast<Match>().Select(m => m.Value).OrderBy(v => v);
                var actual = Placeholder.Matches(hebrew[key]).Cast<Match>().Select(m => m.Value).OrderBy(v => v);
                Assert.True(expected.SequenceEqual(actual), "Placeholders differ for " + key);
            }
        }

        private static System.Collections.Generic.Dictionary<string, string> Load(string fileName)
        {
            var path = Path.Combine(RepositoryRoot(), "src", "WinTool.App", "Localization", fileName);
            return XDocument.Load(path).Root.Elements("data")
                .ToDictionary(e => (string)e.Attribute("name"), e => (string)e.Element("value"));
        }

        private static string RepositoryRoot()
        {
            var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (directory != null && !File.Exists(Path.Combine(directory.FullName, "WinTool.sln")))
            {
                directory = directory.Parent;
            }

            if (directory == null) throw new InvalidOperationException("WinTool.sln not found above " + AppDomain.CurrentDomain.BaseDirectory);
            return directory.FullName;
        }
    }
}
