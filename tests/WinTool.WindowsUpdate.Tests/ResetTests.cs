using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WinTool.WindowsUpdate.Logic;
using Xunit;

namespace WinTool.WindowsUpdate.Tests
{
    public class ResetTests
    {
        private sealed class FakeServices : IServiceControl
        {
            public List<string> Calls { get; } = new List<string>();
            public HashSet<string> Running { get; } = new HashSet<string>(WindowsUpdateReset.Services);
            public void Stop(string service) { Calls.Add("stop " + service); Running.Remove(service); }
            public void Start(string service) { Calls.Add("start " + service); Running.Add(service); }
            public bool IsRunning(string service) => Running.Contains(service);
        }

        private sealed class FakeFolders : IFolderOperations
        {
            public HashSet<string> Existing { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public List<string> Calls { get; } = new List<string>();
            public int DeleteFailures { get; set; }
            public bool Exists(string path) => Existing.Contains(path);
            public void DeleteRecursive(string path)
            {
                Calls.Add("delete " + path);
                if (DeleteFailures-- > 0) throw new IOException("file in use");
                Existing.Remove(path);
            }
            public void Create(string path) { Calls.Add("create " + path); Existing.Add(path); }
            public void Move(string from, string to) { Calls.Add("move " + from + " -> " + to); Existing.Remove(from); Existing.Add(to); }
            public int CountEntries(string path) => 0;
        }

        private static readonly string Root = Path.Combine(Path.GetTempPath(), "FakeWindows");

        private static WindowsUpdateReset Reset(FakeServices services, FakeFolders folders) =>
            new WindowsUpdateReset(services, folders, new ListLog(), Root, () => new DateTime(2026, 11, 6, 10, 0, 0));

        [Fact]
        public void Stops_services_rebuilds_the_cache_moves_catroot2_and_starts_again()
        {
            var services = new FakeServices();
            var folders = new FakeFolders();
            var reset = Reset(services, folders);
            folders.Existing.Add(reset.SoftwareDistributionPath);
            folders.Existing.Add(reset.Catroot2Path);

            var result = reset.Run();

            Assert.True(result.Succeeded, result.Message);
            Assert.True(folders.Exists(reset.SoftwareDistributionPath));
            Assert.Equal(reset.Catroot2Path + ".bak-20261106-100000", result.Catroot2Backup);
            Assert.StartsWith("stop", services.Calls.First());
            Assert.StartsWith("start", services.Calls.Last());
            Assert.True(services.Calls.IndexOf("stop cryptsvc") < services.Calls.IndexOf("start cryptsvc"));
        }

        [Fact]
        public void Only_the_two_windows_folders_are_touched()
        {
            var folders = new FakeFolders();
            var reset = Reset(new FakeServices(), folders);
            folders.Existing.Add(reset.SoftwareDistributionPath);
            reset.Run();
            Assert.All(folders.Calls, call => Assert.True(
                call.Contains(Path.Combine(Root, "SoftwareDistribution")) || call.Contains(Path.Combine(Root, "System32", "catroot2")), call));
        }

        [Fact]
        public void A_locked_cache_is_tried_three_times_then_reported()
        {
            var folders = new FakeFolders { DeleteFailures = 5 };
            var reset = Reset(new FakeServices(), folders);
            folders.Existing.Add(reset.SoftwareDistributionPath);

            var result = reset.Run();

            Assert.False(result.Succeeded);
            Assert.Equal(3, folders.Calls.Count(c => c.StartsWith("delete")));
            Assert.Contains(result.Warnings, w => w.Contains("SoftwareDistribution"));
        }

        [Fact]
        public void A_missing_cache_is_created_and_missing_catroot2_is_fine()
        {
            var folders = new FakeFolders();
            var reset = Reset(new FakeServices(), folders);
            var result = reset.Run();
            Assert.True(result.Succeeded);
            Assert.Null(result.Catroot2Backup);
        }

        [Fact]
        public void Services_that_do_not_come_back_are_reported()
        {
            var services = new FakeServices();
            var folders = new FakeFolders();
            var reset = new WindowsUpdateReset(new StubbornServices(), folders, new ListLog(), Root, null);
            var result = reset.Run();
            Assert.False(result.Succeeded);
            Assert.Contains(result.Warnings, w => w.Contains("wuauserv"));
        }

        private sealed class StubbornServices : IServiceControl
        {
            public void Stop(string service) { }
            public void Start(string service) => throw new InvalidOperationException("disabled");
            public bool IsRunning(string service) => false;
        }
    }
}
