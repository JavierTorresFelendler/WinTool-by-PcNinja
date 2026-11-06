// Ported from Win-Upgrade-Tool-by-PcNinja tests/PcNinja.Win11Upgrade.SmokeTests (see docs/UPSTREAM-SYNC.md).
using WinTool.Core.Json;
using WinTool.Upgrade;

namespace WinTool.Upgrade.Tests.Checks;

internal static class IsoMountCommandChecks
{
    // Nonexistent fixture paths and child-process functions only: the real Storage
    // cmdlets are never invoked, and no ISO, disk, Setup or filesystem media is used.
    internal static async Task RunAsync()
    {
        const string path = @"C:\PcNinja mount fixture\מבחן O'Brien $literal 窗口.iso";
        await CheckAsync(path, attached: true, attachIfMissing: true);
        await CheckAsync(path, attached: true, attachIfMissing: false);
        await CheckAsync(path, attached: false, attachIfMissing: false);
        await CheckAsync(path, attached: false, attachIfMissing: true);
        Console.WriteLine("PASS: Generated hidden PowerShell exact-path Unicode/apostrophe JSON roundtrip; attached lookup, missing lookup and fake single-attach branches. No actual mount cmdlets used.");
    }

    private static async Task CheckAsync(string path, bool attached, bool attachIfMissing)
    {
        const string device = @"\\.\CDROM-PcNinja-FAKE";
        const string volume = @"\\?\Volume{ca17dbbf-681f-4644-9921-63e16e0cbeea}\";
        var expectedMounts = attached || !attachIfMissing ? 0 : 1;
        var prefix = $$"""
            $ErrorActionPreference='Stop';
            $script:expected='{{Quote(path)}}';
            $script:attached=${{(attached ? "true" : "false")}};
            $script:allowMount=${{(attachIfMissing && !attached ? "true" : "false")}};
            $script:mountCalls=0;
            function Get-DiskImage {
                [CmdletBinding()]param([Parameter(Mandatory=$true)][string]$ImagePath)
                if($ImagePath -cne $script:expected){throw 'Fixture exact ImagePath changed'}
                [pscustomobject]@{ImagePath=$ImagePath;Attached=$script:attached;DevicePath='{{Quote(device)}}'}
            }
            function Get-Volume {
                [CmdletBinding()]param([Parameter(ValueFromPipeline=$true)]$InputObject)
                process {
                    if(-not $script:attached -or $InputObject.ImagePath -cne $script:expected){throw 'Fixture volume requested for unattached/different image'}
                    [pscustomobject]@{DriveLetter='Q';UniqueId='{{Quote(volume)}}'}
                }
            }
            function Mount-DiskImage {
                [CmdletBinding()]param([Parameter(Mandatory=$true)][string]$ImagePath)
                if(-not $script:allowMount -or $ImagePath -cne $script:expected -or $script:attached){throw 'Fixture forbidden mount invocation'}
                $script:mountCalls++;
                if($script:mountCalls -ne 1){throw 'Fixture duplicate mount invocation'}
                $script:attached=$true;
                [pscustomobject]@{ImagePath=$ImagePath;Attached=$true;DevicePath='{{Quote(device)}}'}
            }
            """;
        // A lookup of an absent image exits the child with JSON null. The mock
        // Mount function throws if that branch attempts any attachment at all.
        var suffix = !attached && !attachIfMissing ? "" :
            ";if($script:mountCalls -ne " + expectedMounts + "){throw 'Fixture wrong mount count'}";
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var json = (await UpgradeEngine.RunPowerShellAsync(prefix + "\n" +
            UpgradeEngine.BuildIsoMountCommand(path, attachIfMissing) + suffix, timeout.Token)).Trim();
        if (!attached && !attachIfMissing)
        {
            if (json != "null") throw new Exception("An absent ISO lookup did not return exact JSON null.");
            return;
        }

        var document = JsonData.Parse(json) as Dictionary<string, object>
            ?? throw new Exception("The generated mount JSON is not an object.");
        foreach (var property in new[] { "ImagePath", "DevicePath", "VolumeId", "Root", "MountedNow" })
            if (!document.Keys.Contains(property, StringComparer.Ordinal))
                throw new Exception($"The generated mount JSON lost its exact {property} contract.");
        var mount = JsonData.Deserialize<IsoMountIdentity>(json)
            ?? throw new Exception("The generated mount JSON deserialized to null.");
        if (mount.ImagePath != path || mount.DevicePath != device || mount.VolumeId != volume ||
            mount.Root != @"Q:\" || mount.MountedNow != (!attached && attachIfMissing) || !mount.CanReuse)
            throw new Exception("The generated mount helper changed exact Unicode/path identity, JSON casing or attachment state.");
    }

    private static string Quote(string value) => value.Replace("'", "''");
}
