using System.Diagnostics;
using WinTool.Core.Security;

namespace WinTool.Office;

/// <summary>Everything the deployment does outside the process, so tests can replace it.</summary>
public interface IOfficeTools
{
    Task<string> GetStringAsync(string url, CancellationToken token);
    Task DownloadFileAsync(string url, string path, CancellationToken token);
    bool IsMicrosoftSigned(string path, out string signer);

    /// <summary>Runs a program hidden and returns its exit code. killOnCancel stops it when the token is cancelled.</summary>
    Task<int> RunAsync(string file, string arguments, string workingDirectory, bool killOnCancel, CancellationToken token);
}

public sealed class OfficeTools : IOfficeTools
{
    private static readonly HttpClient Http = CreateHttp();

    public Task<string> GetStringAsync(string url, CancellationToken token) => GetStringCoreAsync(url, token);

    private static async Task<string> GetStringCoreAsync(string url, CancellationToken token)
    {
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            using (var response = await Http.GetAsync(url, timeout.Token).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task DownloadFileAsync(string url, string path, CancellationToken token)
    {
        using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            using (var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
            using (var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                await input.CopyToAsync(output, 81920, token).ConfigureAwait(false);
            }
        }
    }

    public bool IsMicrosoftSigned(string path, out string signer) => AuthenticodeVerifier.IsTrustedMicrosoftFile(path, out signer);

    public async Task<int> RunAsync(string file, string arguments, string workingDirectory, bool killOnCancel, CancellationToken token)
    {
        using (var process = new Process())
        {
            process.StartInfo = new ProcessStartInfo(file, arguments)
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            process.EnableRaisingEvents = true;
            var exited = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            process.Exited += (sender, e) => exited.TrySetResult(true);
            process.Start();
            using (token.Register(() =>
            {
                if (!killOnCancel) return;
                try { if (!process.HasExited) process.Kill(); } catch (InvalidOperationException) { }
            }))
            {
                await exited.Task.ConfigureAwait(false);
            }

            process.WaitForExit();
            if (killOnCancel) token.ThrowIfCancellationRequested();
            return process.ExitCode;
        }
    }

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WinTool-by-PcNinja/" + WinTool.Core.ProductInfo.Version);
        return http;
    }
}
