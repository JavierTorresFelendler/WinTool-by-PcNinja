using System.Net.Http;

namespace WinTool.Office.Tests;

/// <summary>Records what the deployment would run; can create the files the real tools create.</summary>
internal sealed class FakeTools : IOfficeTools
{
    public bool PageFails { get; set; }
    public string Page { get; set; } = "https://download.microsoft.com/download/x/officedeploymenttool_20430-20146.exe";
    public bool DownloadFails { get; set; }
    public Func<string, bool> Signed { get; set; } = path => true;
    public int ExtractExit { get; set; }
    public int SetupExit { get; set; }
    public bool DownloadCreatesData { get; set; } = true;
    public List<string> Calls { get; } = new List<string>();
    public List<string> Configurations { get; } = new List<string>();

    public Task<string> GetStringAsync(string url, CancellationToken token)
    {
        Calls.Add("page " + url);
        if (PageFails) throw new HttpRequestException("offline");
        return Task.FromResult(Page);
    }

    public Task DownloadFileAsync(string url, string path, CancellationToken token)
    {
        Calls.Add("download " + url);
        if (DownloadFails) throw new HttpRequestException("connection reset");
        File.WriteAllText(path, "odt");
        return Task.CompletedTask;
    }

    public bool IsMicrosoftSigned(string path, out string signer)
    {
        Calls.Add("verify " + Path.GetFileName(path));
        var ok = Signed(path);
        signer = ok ? "CN=Microsoft Corporation" : "CN=Someone Else";
        return ok;
    }

    public Task<int> RunAsync(string file, string arguments, string workingDirectory, bool killOnCancel, CancellationToken token)
    {
        Calls.Add("run " + Path.GetFileName(file) + " " + arguments.Split(' ')[0] + (killOnCancel ? " cancellable" : " uninterruptible"));
        if (arguments.StartsWith("/quiet /extract:", StringComparison.Ordinal))
        {
            var folder = arguments.Substring("/quiet /extract:".Length).Trim('"');
            if (ExtractExit == 0) File.WriteAllText(Path.Combine(folder, "setup.exe"), "setup");
            return Task.FromResult(ExtractExit);
        }

        var config = arguments.Substring(arguments.IndexOf('"')).Trim('"');
        Configurations.Add(File.ReadAllText(config));
        if (arguments.StartsWith("/download", StringComparison.Ordinal) && DownloadCreatesData && SetupExit == 0)
        {
            var data = Path.Combine(workingDirectory, "Office", "Data", "16.0.1");
            Directory.CreateDirectory(data);
            File.WriteAllText(Path.Combine(data, "stream.x64.x-none.dat"), "office");
        }

        return Task.FromResult(SetupExit);
    }
}
