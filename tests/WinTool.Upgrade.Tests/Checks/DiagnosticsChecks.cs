// Ported from Win-Upgrade-Tool-by-PcNinja tests/PcNinja.Win11Upgrade.SmokeTests (see docs/UPSTREAM-SYNC.md).
using WinTool.Upgrade;

namespace WinTool.Upgrade.Tests.Checks;

internal static class DiagnosticsChecks
{
    internal static void Run()
    {
        foreach (var code in new[] { 0, 1, unchecked((int)0xC0000005), 0x40010004 })
            foreach (var automaticRestart in new[] { false, true })
                if (new UpgradeResult(code, "unused-setup.exe").NeedsManualRestartNotice(automaticRestart) !=
                    (code == 0 && !automaticRestart))
                    throw new Exception("Manual-restart guidance was shown for a failure/transition or wrong restart policy.");
        const string xml = "#< CLIXML\r\n<Objs xmlns='http://schemas.microsoft.com/powershell/2004/04'><Obj S='progress'><S>Loading</S></Obj><S S='Error'>PCNINJA_ISO_VOLUME_TIMEOUT_x000D__x000A_</S></Objs>";
        var timeout = PowerShellDiagnostics.MountFailureMessage(xml);
        if (!timeout.Contains("30 seconds") || !timeout.Contains("Refresh") || timeout.Contains("CLIXML"))
            throw new Exception("The genuine CLIXML mount timeout was not explained safely.");
        var decoded = PowerShellDiagnostics.MountFailureMessage(
            "#< CLIXML\n<Objs><S S='Error'>Device_x0020_not ready_x000D__x000A_At line 1</S></Objs>");
        if (!decoded.Contains("Device not ready") || decoded.Contains("At line") || decoded.Contains("_x"))
            throw new Exception("Serialized PowerShell errors were not decoded and shortened.");
        foreach (var input in new[]
        {
            "#< CLIXML\n<Objs><Obj S='progress'><S>Loading</S></Obj></Objs>",
            "#< CLIXML\n<broken", "", "#< CLIXML\n<!DOCTYPE x [<!ENTITY x SYSTEM 'file:///does-not-exist'>]><Objs>&x;</Objs>"
        })
        {
            var message = PowerShellDiagnostics.MountFailureMessage(input);
            if (message.Contains("CLIXML") || message.Contains("<") || !message.Contains("Refresh"))
                throw new Exception("Unusable serialized output did not use the safe generic diagnostic.");
        }
        var lowSpace = UpgradeEngine.SystemDriveSpaceMessage("Z:\\", 11L * 1024 * 1024 * 1024);
        if (!lowSpace.Contains("Z:\\") || !lowSpace.Contains("11.00") || !lowSpace.Contains("20.00") ||
            !lowSpace.Contains("9.00") || !lowSpace.Contains("Refresh"))
            throw new Exception("Low-space guidance omitted the actual drive, available/required/missing space or next action.");
        Console.WriteLine("PASS: Safe CLIXML decoding, bounded XML/DTD rejection, mount-timeout and low-space guidance.");
    }
}
