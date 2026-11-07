// Ported from Win-Upgrade-Tool-by-PcNinja tests/PcNinja.Win11Upgrade.SmokeTests (see docs/UPSTREAM-SYNC.md).
using WinTool.Core.Json;
using WinTool.Upgrade;

namespace WinTool.Upgrade.Tests.Checks;

internal static class IsoMountCommandChecks
{
    private const string FixturePath = @"C:\PcNinja mount fixture\מבחן O'Brien $literal 窗口.iso";
    private const string FixtureDevice = @"\\.\CDROM-PcNinja-FAKE";
    private const string FixtureVolume = @"\\?\Volume{ca17dbbf-681f-4644-9921-63e16e0cbeea}\";

    // The four Windows PowerShell 5.1 launches below normally take about 4 s together.
    // GitHub's hosted Windows runners run both target frameworks' test hosts at once,
    // and under that load a single cold start once took more than 20 s (Release, commit 52ea475: the first launch was
    // cancelled while every other test in that host ran ~25x slower than usual).
    // The budget is per launch and only guards against a hung child, so CI gets a
    // generous one; developer machines keep the short one so a real hang shows quickly.
    private static readonly TimeSpan LaunchTimeout = RunningOnCi() ? TimeSpan.FromSeconds(120) : TimeSpan.FromSeconds(20);

    private static bool RunningOnCi() =>
        string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The generated command text and the JSON contract it feeds, without starting any
    /// process. Runs on every platform.
    /// </summary>
    internal static void RunCommandText()
    {
        foreach (var attachIfMissing in new[] { false, true })
        {
            var command = UpgradeEngine.BuildIsoMountCommand(FixturePath, attachIfMissing);
            // BuildIsoMountCommand resolves the path first; on Linux a C:\ path is relative.
            var literal = "$p='" + Path.GetFullPath(FixturePath).Replace("'", "''") + "';";
            Expect(Count(command, literal) == 1, "The ISO path is not one exact single-quoted literal with doubled apostrophes.");
            Expect(command.IndexOf('"') < 0, "The command contains a double-quoted (expandable) string.");
            Expect(Count(command, "$p=") == 1, "The ISO path variable is assigned more than once.");
            Expect(command.StartsWith("$ProgressPreference='SilentlyContinue';[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false);", StringComparison.Ordinal),
                "The command lost its silent progress or UTF-8 output preamble.");
            Expect(command.Contains("$d=Get-DiskImage -ImagePath $p -ErrorAction SilentlyContinue;"), "The first lookup is not by the exact path.");
            Expect(command.Contains("|ConvertTo-Json -Compress))"), "The result is not written as compact JSON.");
            foreach (var key in new[] { "ImagePath=", "DevicePath=", "VolumeId=", "Root=", "MountedNow=" })
                Expect(Count(command, key) == 1, $"The JSON key {key.TrimEnd('=')} is missing or repeated.");

            var mounts = Count(command, "Mount-DiskImage");
            if (attachIfMissing)
            {
                Expect(mounts == 1 && command.Contains("Mount-DiskImage -ImagePath $p -ErrorAction Stop|Out-Null;$mounted=$true;"),
                    "The attach branch does not mount the exact path exactly once.");
                Expect(command.Contains("AddSeconds(30)"), "The attach branch lost its 30 s volume wait.");
                Expect(command.Contains("throw 'PCNINJA_ISO_VOLUME_TIMEOUT'"), "The attach branch does not fail when no volume appears.");
                Expect(!command.Contains("Out.Write('null')"), "The attach branch can report an absent image.");
            }
            else
            {
                Expect(mounts == 0, "The lookup-only command can mount an image.");
                Expect(command.Contains("AddSeconds(0)"), "The lookup-only command waits for a volume.");
                Expect(Count(command, "[Console]::Out.Write('null');exit 0") == 2, "The lookup-only command does not return null for an absent image or volume.");
                Expect(!command.Contains("PCNINJA_ISO_VOLUME_TIMEOUT"), "The lookup-only command throws instead of returning null.");
            }
        }

        // The output shape Windows PowerShell 5.1 ConvertTo-Json -Compress writes (it
        // escapes the apostrophe as \u0027), and the literal null of the lookup branch.
        Expect(JsonData.Deserialize<IsoMountIdentity>("null") is null, "JSON null did not mean an absent image.");
        var json = "{\"ImagePath\":\"" + JsonEscape(FixturePath).Replace("'", "\\u0027") + "\",\"DevicePath\":\"" + JsonEscape(FixtureDevice) +
            "\",\"VolumeId\":\"" + JsonEscape(FixtureVolume) + "\",\"Root\":\"Q:\\\\\",\"MountedNow\":true}";
        CheckIdentity(json, FixturePath, mountedNow: true);
        Console.WriteLine("PASS: Mount command text: exact single-quoted Unicode/apostrophe path, no expandable strings, lookup never mounts, attach mounts once with a bounded wait; PowerShell JSON and null contract. No process started.");
    }

    // Nonexistent fixture paths and child-process functions only: the real Storage
    // cmdlets are never invoked, and no ISO, disk, Setup or filesystem media is used.
    internal static async Task RunAsync()
    {
        const string path = FixturePath;
        await CheckAsync(path, attached: true, attachIfMissing: true);
        await CheckAsync(path, attached: true, attachIfMissing: false);
        await CheckAsync(path, attached: false, attachIfMissing: false);
        await CheckAsync(path, attached: false, attachIfMissing: true);
        Console.WriteLine("PASS: Generated hidden PowerShell exact-path Unicode/apostrophe JSON roundtrip; attached lookup, missing lookup and fake single-attach branches. No actual mount cmdlets used.");
    }

    private static async Task CheckAsync(string path, bool attached, bool attachIfMissing)
    {
        const string device = FixtureDevice;
        const string volume = FixtureVolume;
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
        using var timeout = new CancellationTokenSource(LaunchTimeout);
        var started = DateTime.UtcNow;
        string json;
        try
        {
            json = (await UpgradeEngine.RunPowerShellAsync(prefix + "\n" +
                UpgradeEngine.BuildIsoMountCommand(path, attachIfMissing) + suffix, timeout.Token)).Trim();
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"Windows PowerShell did not finish the mock mount case attached={attached}, " +
                $"attachIfMissing={attachIfMissing} within {LaunchTimeout.TotalSeconds:0} s " +
                $"(waited {(DateTime.UtcNow - started).TotalSeconds:0.0} s, CI={RunningOnCi()}). The child process was terminated.");
        }
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
        CheckIdentity(json, path, mountedNow: !attached && attachIfMissing);
    }

    private static void CheckIdentity(string json, string path, bool mountedNow)
    {
        var mount = JsonData.Deserialize<IsoMountIdentity>(json)
            ?? throw new Exception("The generated mount JSON deserialized to null.");
        if (mount.ImagePath != path || mount.DevicePath != FixtureDevice || mount.VolumeId != FixtureVolume ||
            mount.Root != @"Q:\" || mount.MountedNow != mountedNow || !mount.CanReuse)
            throw new Exception("The generated mount helper changed exact Unicode/path identity, JSON casing or attachment state.");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        for (var index = text.IndexOf(value, StringComparison.Ordinal); index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }

    private static string JsonEscape(string value) => value.Replace("\\", "\\\\");

    private static string Quote(string value) => value.Replace("'", "''");
}
