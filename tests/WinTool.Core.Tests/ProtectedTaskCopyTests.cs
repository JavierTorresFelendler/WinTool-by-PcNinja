using System;
using System.IO;
using WinTool.Core.Security;
using Xunit;

namespace WinTool.Core.Tests
{
    public class ProtectedTaskCopyTests
    {
        [Fact]
        public void Provisioned_target_is_independent_of_original_and_reused_only_when_bytes_match()
        {
            using (var temp = new TempDirectory())
            {
                var source = temp.File("source.exe"); File.WriteAllText(source, "file-only fixture");
                Func<string> create = () => ProtectedTaskCopy.Provision(source, temp.File("Scheduler"), path => File.OpenRead(path),
                    path => Directory.CreateDirectory(path), path => File.Create(path), _ => true);
                var target = create();
                Assert.Equal(target, create());
                File.Delete(source);
                Assert.Equal("file-only fixture", File.ReadAllText(target));
                File.WriteAllText(source, "file-only fixture"); File.WriteAllText(target, "modified");
                Assert.Throws<IOException>(() => create());
            }
        }

        [Fact]
        public void Failed_publication_removes_staging_file_and_preserves_source()
        {
            using (var temp = new TempDirectory())
            {
                var source = temp.File("source.exe"); File.WriteAllText(source, "fixture");
                Assert.Throws<IOException>(() => ProtectedTaskCopy.Provision(source, temp.File("Scheduler"), File.OpenRead,
                    path => Directory.CreateDirectory(path), path => File.Create(path), _ => false));
                Assert.Empty(Directory.GetFiles(temp.Path, "*.partial", SearchOption.AllDirectories));
                Assert.Equal("fixture", File.ReadAllText(source));
            }
        }

        [Fact]
        public void Retirement_keeps_referenced_or_busy_copies_and_retries_later()
        {
            using (var temp = new TempDirectory())
            {
                var directory = temp.File(new string('A', 64)); Directory.CreateDirectory(directory);
                var exe = Path.Combine(directory, ReleaseFiles.Executable); File.WriteAllText(exe, "fixture");
                ProtectedTaskCopy.Retire(temp.Path, new[] { exe }, _ => true); Assert.True(File.Exists(exe));
                using (var held = new FileStream(exe, FileMode.Open, FileAccess.Read, FileShare.None))
                { ProtectedTaskCopy.Retire(temp.Path, new string[0], _ => true); Assert.True(File.Exists(exe)); }
                ProtectedTaskCopy.Retire(temp.Path, new string[0], _ => true); Assert.False(File.Exists(exe));
            }
        }
    }
}
