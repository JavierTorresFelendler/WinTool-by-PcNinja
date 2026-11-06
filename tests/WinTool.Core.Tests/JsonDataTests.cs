using System;
using System.Collections.Generic;
using WinTool.Core.Json;
using Xunit;

namespace WinTool.Core.Tests
{
    public class JsonDataTests
    {
        public sealed class Mount
        {
            public Mount(string imagePath, string root, bool mountedNow)
            {
                ImagePath = imagePath;
                Root = root;
                MountedNow = mountedNow;
            }

            public string ImagePath { get; }
            public string Root { get; }
            public bool MountedNow { get; }
        }

        public enum Kind { Alpha, Beta }

        public sealed class Settable
        {
            public string Name { get; set; }
            public int Count { get; set; }
            public Kind Kind { get; set; }
            public DateTimeOffset? When { get; set; }
            public List<string> Tags { get; set; }
            public long[] Sizes { get; set; }
        }

        [Fact]
        public void Reads_powershell_output_into_a_constructor_type()
        {
            var mount = JsonData.Deserialize<Mount>("{\"ImagePath\":\"C:\\\\ISO\\\\Win11.iso\",\"DevicePath\":\"x\",\"Root\":\"E:\\\\\",\"MountedNow\":true}");
            Assert.Equal(@"C:\ISO\Win11.iso", mount.ImagePath);
            Assert.Equal(@"E:\", mount.Root);
            Assert.True(mount.MountedNow);
        }

        [Fact]
        public void Reads_settable_properties_lists_enums_and_dates()
        {
            var value = JsonData.Deserialize<Settable>("\uFEFF{ \"name\": \"עברית\", \"count\": 3, \"kind\": \"Beta\", \"when\": \"2026-11-06T10:00:00+02:00\", \"tags\": [\"a\",\"b\"], \"sizes\": [1, 5000000000] }");
            Assert.Equal("עברית", value.Name);
            Assert.Equal(3, value.Count);
            Assert.Equal(Kind.Beta, value.Kind);
            Assert.Equal(new DateTimeOffset(2026, 11, 6, 10, 0, 0, TimeSpan.FromHours(2)), value.When);
            Assert.Equal(new[] { "a", "b" }, value.Tags);
            Assert.Equal(5000000000L, value.Sizes[1]);
        }

        [Fact]
        public void Null_and_missing_values_are_defaults()
        {
            Assert.Null(JsonData.Deserialize<Mount>("null"));
            var value = JsonData.Deserialize<Settable>("{\"name\":null}");
            Assert.Null(value.Name);
            Assert.Equal(0, value.Count);
        }

        [Fact]
        public void Round_trips_through_the_writer()
        {
            var original = new Settable { Name = "x \"quoted\"", Count = 2, Kind = Kind.Alpha, Tags = new List<string> { "t" } };
            var copy = JsonData.Deserialize<Settable>(JsonData.Serialize(original));
            Assert.Equal(original.Name, copy.Name);
            Assert.Equal(2, copy.Count);
            Assert.Equal(new[] { "t" }, copy.Tags);
        }

        [Theory]
        [InlineData("{")]
        [InlineData("{\"a\" 1}")]
        [InlineData("[1,]")]
        [InlineData("\"open")]
        [InlineData("{} extra")]
        public void Broken_json_is_a_format_error(string json)
        {
            Assert.Throws<FormatException>(() => JsonData.Parse(json));
        }
    }
}
