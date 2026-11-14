using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WinTool.Cli;
using WinTool.Core;
using WinTool.Core.Updates;
using Xunit;

namespace WinTool.WindowsUpdate.Tests
{
    public sealed class AppUpdateWorkerRefreshTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Successful_swap_queues_refresh_from_new_image_without_executing_a_fixture(bool launchFails)
        {
            var folder = Path.Combine(Path.GetTempPath(), "WinToolWorkerRefreshTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var running = Path.Combine(folder, ReleaseFiles.Executable);
                File.WriteAllText(running, "old fixture; never executable");
                var web = new FakeWeb();
                var updater = new AppUpdater(web, Path.Combine(folder, "downloads"),
                    _ => new ExeIdentity(ProductInfo.Name, "99.0.0.9000"));
                var attempts = 0;
                var output = new StringWriter(); var error = new StringWriter();
                var command = new AppUpdateCommands(() => updater, running, output, error, path =>
                {
                    attempts++;
                    Assert.Equal(running, path);
                    Assert.Equal("new fixture; never executable", File.ReadAllText(path));
                    if (launchFails) throw new System.ComponentModel.Win32Exception("fixture launch refusal");
                });
                Assert.Equal(0, command.Run("install", new[] { "--yes", "--json" }));
                Assert.Equal(1, attempts);
                Assert.Contains("\"result\": \"Updated\"", output.ToString());
                Assert.Contains("\"workerRefreshQueued\": " + (launchFails ? "false" : "true"), output.ToString());
            }
            finally { Directory.Delete(folder, true); }
        }

        private sealed class FakeWeb : IUpdateWeb
        {
            private static readonly byte[] Bytes = Encoding.UTF8.GetBytes("new fixture; never executable");
            private const string Base = "https://github.com/JavierTorresFelendler/WinTool-by-PcNinja/releases/download/v99.0.0/";
            public Task<string> GetStringAsync(string url, CancellationToken token)
            {
                if (url.EndsWith(ReleaseFeed.ChecksumsFile, StringComparison.Ordinal))
                {
                    using (var hash = SHA256.Create())
                        return Task.FromResult(BitConverter.ToString(hash.ComputeHash(Bytes)).Replace("-", "") + "  " + ReleaseFiles.Executable);
                }
                var assets = new System.Collections.Generic.List<string>();
                foreach (var name in new[] { ReleaseFiles.Executable, ReleaseFiles.Installer, ReleaseFeed.ChecksumsFile })
                    assets.Add("{\"name\":\"" + name + "\",\"browser_download_url\":\"" + Base + name + "\"}");
                return Task.FromResult("[{\"tag_name\":\"v99.0.0\",\"draft\":false,\"prerelease\":false,\"assets\":[" + string.Join(",", assets) + "]}]");
            }
            public Task DownloadAsync(string url, string path, IProgress<long> progress, CancellationToken token)
            { File.WriteAllBytes(path, Bytes); return Task.CompletedTask; }
        }
    }
}
