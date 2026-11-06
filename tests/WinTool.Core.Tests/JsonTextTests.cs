using System;
using System.Collections.Generic;
using WinTool.Core.Json;
using Xunit;

namespace WinTool.Core.Tests
{
    public class JsonTextTests
    {
        private enum Color { Red }

        [Fact]
        public void Objects_use_camel_case_and_stable_formats()
        {
            var json = JsonText.Serialize(new { Name = "x", Count = 3, Ok = true, When = new DateTime(2026, 11, 6, 10, 0, 0), Kind = Color.Red, Missing = (string)null });
            Assert.Contains("\"name\": \"x\"", json);
            Assert.Contains("\"count\": 3", json);
            Assert.Contains("\"ok\": true", json);
            Assert.Contains("\"when\": \"2026-11-06T10:00:00\"", json);
            Assert.Contains("\"kind\": \"Red\"", json);
            Assert.Contains("\"missing\": null", json);
        }

        [Fact]
        public void Strings_are_escaped_and_hebrew_is_kept()
        {
            var json = JsonText.Serialize("עדכון \"מצטבר\"\n\\");
            Assert.Equal("\"עדכון \\\"מצטבר\\\"\\n\\\\\"", json);
        }

        [Fact]
        public void Lists_and_dictionaries()
        {
            Assert.Equal("[]", JsonText.Serialize(new int[0]));
            var json = JsonText.Serialize(new Dictionary<string, object> { { "Keep-Case", 1.5 }, { "list", new[] { 1, 2 } } });
            Assert.Contains("\"Keep-Case\": 1.5", json);
            Assert.Contains("\"list\": [", json);
        }
    }
}
