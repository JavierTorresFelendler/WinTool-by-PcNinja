using WinTool.Upgrade.Tests.Checks;
using Xunit;

namespace WinTool.Upgrade.Tests;

/// <summary>
/// The Windows Upgrade tool's smoke checks, one test per check group. They never
/// download an ISO, mount media or launch Windows Setup. Groups that need Windows
/// (registry, Windows path rules, native file proof, PowerShell, Authenticode) pass trivially elsewhere.
/// </summary>
public class UpgradeChecksTests
{
    private static bool OnWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

    [Fact] public void Engine() => EngineChecks.Run();
    [Fact] public Task Transfer() => TransferChecks.RunAsync();
    [Fact] public void Diagnostics() => DiagnosticsChecks.Run();
    [Fact] public void Setup_outcome() => SetupOutcomeChecks.Run();
    [Fact] public void Windows10_target() => Windows10TargetChecks.Run();
    [Fact] public void Quiet_setup() => QuietSetupChecks.Run();
    [Fact] public void Manual_upgrade() => ManualUpgradeChecks.Run();
    [Fact] public Task Download_sources() => DownloadSourceChecks.RunAsync();

    [Fact]
    public void Safety_and_images()
    {
        if (OnWindows) SafetyImageChecks.Run();
    }

    [Fact]
    public void Manual_server_upgrade()
    {
        if (OnWindows) ManualServerUpgradeChecks.Run();
    }

    [Fact]
    public void Iso_file_proof()
    {
        if (OnWindows) IsoFileProofChecks.Run();
    }

    [Fact]
    public async Task Iso_validation_session()
    {
        if (OnWindows) await IsoValidationSessionChecks.RunAsync();
    }

    [Fact] public void Iso_mount_command_text() => IsoMountCommandChecks.RunCommandText();

    /// <summary>Runs the generated script in real Windows PowerShell 5.1 against mock Storage cmdlets.</summary>
    [Fact]
    public async Task Iso_mount_command()
    {
        if (OnWindows) await IsoMountCommandChecks.RunAsync();
    }
}
