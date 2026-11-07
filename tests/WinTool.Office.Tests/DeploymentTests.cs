using WinTool.Core.Operations;
using Xunit;

namespace WinTool.Office.Tests;

public sealed class DeploymentTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "WinToolOfficeTests", Guid.NewGuid().ToString("N"));
    private readonly FakeTools tools = new FakeTools();

    public DeploymentTests() => Directory.CreateDirectory(root);

    public void Dispose()
    {
        try { Directory.Delete(root, true); } catch (IOException) { }
    }

    private string Work => Path.Combine(root, "work");

    private OfficeDeployment Create(bool busy = false)
    {
        Directory.CreateDirectory(Work);
        return new OfficeDeployment(tools, () => busy ? null : new Released(), Work);
    }

    private sealed class Released : IDisposable
    {
        public void Dispose() { }
    }

    [Fact]
    public async Task Install_uses_the_latest_verified_tool_and_never_interrupts_setup()
    {
        var outcome = await Create().InstallAsync(new OfficeRequest(), null, CancellationToken.None);
        Assert.Equal(new[]
        {
            "page " + OdtSource.DownloadPage,
            "download " + tools.Page,
            "verify officedeploymenttool.exe",
            "run officedeploymenttool.exe /quiet cancellable",
            "verify setup.exe",
            "run setup.exe /configure uninterruptible",
        }, tools.Calls);
        Assert.Equal(OfficeConfiguration.Build(new OfficeRequest()), tools.Configurations.Single());
        Assert.Equal(tools.Page, outcome.ToolUrl);
        Assert.Empty(Directory.GetFileSystemEntries(Work));
    }

    private sealed class Collect : IProgress<OfficeProgress>
    {
        public List<string> Messages { get; } = new List<string>();
        public void Report(OfficeProgress value) { lock (Messages) Messages.Add(value.Message); }
    }

    [Fact]
    public async Task A_running_installation_reports_elapsed_time_and_folder_growth()
    {
        tools.ConfigureDelay = TimeSpan.FromMilliseconds(400);
        var deployment = Create();
        deployment.InstallHeartbeat = TimeSpan.FromMilliseconds(50);
        long bytes = 0;
        deployment.OfficeFolderBytes = () => bytes += 10 * 1048576;
        var progress = new Collect();
        await deployment.InstallAsync(new OfficeRequest { Silent = true }, progress, CancellationToken.None);
        Assert.Contains(progress.Messages, m => m.StartsWith("Installing Office silently", StringComparison.Ordinal));
        Assert.Contains(progress.Messages, m => m.StartsWith("Office setup is running (0:00); Microsoft Office folder", StringComparison.Ordinal) && m.Contains("MB since the start"));
        Assert.Contains(progress.Messages, m => m.StartsWith("Office setup finished after", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unsigned_tool_never_runs()
    {
        tools.Signed = path => false;
        var error = await Assert.ThrowsAsync<OfficeDeploymentException>(() => Create().InstallAsync(new OfficeRequest(), null, CancellationToken.None));
        Assert.Equal(OfficeFailure.ToolNotTrusted, error.Failure);
        Assert.DoesNotContain(tools.Calls, call => call.StartsWith("run ", StringComparison.Ordinal));
        Assert.Empty(Directory.GetFileSystemEntries(Work));
    }

    [Fact]
    public async Task An_unsigned_setup_exe_never_runs()
    {
        tools.Signed = path => Path.GetFileName(path) != "setup.exe";
        var error = await Assert.ThrowsAsync<OfficeDeploymentException>(() => Create().InstallAsync(new OfficeRequest(), null, CancellationToken.None));
        Assert.Equal(OfficeFailure.ToolNotTrusted, error.Failure);
        Assert.DoesNotContain(tools.Calls, call => call.StartsWith("run setup.exe", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_failed_download_or_extraction_is_reported()
    {
        tools.DownloadFails = true;
        var download = await Assert.ThrowsAsync<OfficeDeploymentException>(() => Create().InstallAsync(new OfficeRequest(), null, CancellationToken.None));
        Assert.Equal(OfficeFailure.ToolUnavailable, download.Failure);

        tools.DownloadFails = false;
        tools.ExtractExit = 5;
        var extract = await Assert.ThrowsAsync<OfficeDeploymentException>(() => Create().InstallAsync(new OfficeRequest(), null, CancellationToken.None));
        Assert.Equal(OfficeFailure.ToolUnavailable, extract.Failure);
    }

    [Fact]
    public async Task A_setup_failure_keeps_its_exit_code()
    {
        tools.SetupExit = 17002;
        var error = await Assert.ThrowsAsync<OfficeDeploymentException>(() => Create().RemoveAllAsync(false, null, CancellationToken.None));
        Assert.Equal(OfficeFailure.SetupFailed, error.Failure);
        Assert.Equal(17002, error.SetupExitCode);
        Assert.Contains("0x0000426A", error.Message);
        Assert.Empty(Directory.GetFileSystemEntries(Work));
    }

    [Fact]
    public async Task Another_operation_blocks_and_nothing_is_downloaded()
    {
        await Assert.ThrowsAsync<OperationBusyException>(() => Create(busy: true).InstallAsync(new OfficeRequest(), null, CancellationToken.None));
        Assert.Empty(tools.Calls);
    }

    [Fact]
    public async Task Remove_all_uses_the_remove_configuration()
    {
        await Create().RemoveAllAsync(true, null, CancellationToken.None);
        Assert.Equal(OfficeConfiguration.RemoveAll(true), tools.Configurations.Single());
    }

    [Fact]
    public async Task A_package_has_setup_config_script_and_the_office_folder_beside_them()
    {
        var target = Path.Combine(root, "USB");
        var request = new OfficeRequest { Family = OfficeFamily.Ltsc2024 };
        var outcome = await Create().PackageAsync(request, target, null, CancellationToken.None);

        var package = Path.Combine(target, OfficeDeployment.PackageFolderName);
        Assert.Equal(package, outcome.PackageFolder);
        Assert.Equal(OfficeConfiguration.Build(request, package), tools.Configurations.Single());
        Assert.Contains("run setup.exe /download cancellable", tools.Calls);
        Assert.True(File.Exists(Path.Combine(package, "setup.exe")));
        Assert.True(File.Exists(Path.Combine(package, "Office", "Data", "16.0.1", "stream.x64.x-none.dat")));
        // Installs from the Office folder beside setup.exe: no SourcePath, so the package works wherever it is copied.
        Assert.Equal(OfficeConfiguration.Build(request), File.ReadAllText(Path.Combine(package, "Office_Config.xml")));
        Assert.Equal(OfficeDeployment.InstallScript, File.ReadAllText(Path.Combine(package, "Install-Office.bat")));
        Assert.Contains("setup.exe /configure Office_Config.xml", OfficeDeployment.InstallScript);
    }

    [Fact]
    public async Task A_download_that_creates_no_office_data_fails()
    {
        tools.DownloadCreatesData = false;
        var error = await Assert.ThrowsAsync<OfficeDeploymentException>(() =>
            Create().PackageAsync(new OfficeRequest(), Path.Combine(root, "pkg"), null, CancellationToken.None));
        Assert.Equal(OfficeFailure.SetupFailed, error.Failure);
    }

    [Theory]
    [InlineData(@"D:\USB", @"D:\USB\OFFICE-OFFLINE")]
    [InlineData(@"D:\USB\OFFICE-OFFLINE", @"D:\USB\OFFICE-OFFLINE")]
    [InlineData(@"D:\USB\office-offline\", @"D:\USB\office-offline\")]
    public void The_package_folder_is_named_like_the_original(string selected, string expected)
    {
        if (Path.DirectorySeparatorChar != '\\') return;
        Assert.Equal(expected, OfficeDeployment.ResolvePackageFolder(selected));
    }

    [Fact]
    public async Task The_tool_check_downloads_and_verifies_but_runs_nothing()
    {
        var check = await Create().CheckToolAsync(CancellationToken.None);
        Assert.True(check.Latest);
        Assert.True(check.SignedByMicrosoft);
        Assert.Equal(tools.Page, check.Url);
        Assert.DoesNotContain(tools.Calls, call => call.StartsWith("run ", StringComparison.Ordinal));
        Assert.Empty(Directory.GetFileSystemEntries(Work));
    }

    [Fact]
    public async Task Cancelling_before_setup_stops_without_running_it()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().InstallAsync(new OfficeRequest(), null, cancel.Token));
        Assert.DoesNotContain(tools.Calls, call => call.StartsWith("run setup.exe", StringComparison.Ordinal));
    }
}
