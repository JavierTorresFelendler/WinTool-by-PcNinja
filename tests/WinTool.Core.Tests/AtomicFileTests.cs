using System.IO;
using System.Linq;
using System.Text;
using WinTool.Core.IO;
using Xunit;

namespace WinTool.Core.Tests
{
    public class AtomicFileTests
    {
        [Fact]
        public void Creates_new_file_with_utf8_without_bom()
        {
            using (var temp = new TempDirectory())
            {
                var path = temp.File("new.json");
                AtomicFile.WriteAllText(path, "שלום world");
                var bytes = File.ReadAllBytes(path);
                Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
                Assert.Equal("שלום world", Encoding.UTF8.GetString(bytes));
            }
        }

        [Fact]
        public void Replaces_existing_file_and_leaves_no_temporary_files()
        {
            using (var temp = new TempDirectory())
            {
                var path = temp.File("settings.json");
                AtomicFile.WriteAllText(path, "old");
                AtomicFile.WriteAllText(path, "new");
                Assert.Equal("new", File.ReadAllText(path));
                Assert.Single(Directory.GetFiles(temp.Path));
            }
        }

        [Fact]
        public void Creates_missing_directories()
        {
            using (var temp = new TempDirectory())
            {
                var path = Path.Combine(temp.Path, "a", "b", "c.txt");
                AtomicFile.WriteAllText(path, "x");
                Assert.True(File.Exists(path));
            }
        }

        [Fact]
        public void Read_returns_null_for_missing_file()
        {
            using (var temp = new TempDirectory())
            {
                Assert.Null(AtomicFile.ReadAllTextOrNull(temp.File("missing.txt")));
            }
        }
    }
}
