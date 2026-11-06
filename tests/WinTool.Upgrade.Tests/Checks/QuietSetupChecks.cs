// Ported from Win-Upgrade-Tool-by-PcNinja tests/PcNinja.Win11Upgrade.SmokeTests (see docs/UPSTREAM-SYNC.md).
using WinTool.Upgrade;

namespace WinTool.Upgrade.Tests.Checks;

internal static class QuietSetupChecks
{
    internal static void Run()
    {
        foreach (var build in new[] { 19041, 26300 })
        foreach (var file in new[] { "install.wim", "install.esd", "install.swm" })
        foreach (var restart in new[] { false, true })
        foreach (var quiet in new[] { false, true })
        {
            var image = new InstallationImage(2, "Fixture", "Professional", "x64", $"10.0.{build}", "Client", "en-US");
            var source = new MountedSource("X:\\", "X:\\setup.exe", "X:\\sources\\" + file, [image]);
            var arguments = UpgradeEngine.BuildSetupArguments(source, image, restart, quiet);
            if (arguments.Contains(" /quiet") != quiet || arguments.Contains(" /noreboot") != !restart ||
                arguments.Contains("/eula accept") != (build >= 22000) ||
                arguments.Contains("/product server") != (build >= 22000) ||
                !arguments.Contains("/auto upgrade") || !arguments.Contains("/imageindex 2") ||
                !arguments.Contains($"/installfrom \"{source.ImagePath}\"") ||
                arguments.Contains("/auto clean") || arguments.Contains("/auto dataonly") || arguments.Contains("/unattend"))
                throw new Exception("Quiet/restart command matrix changed target profile or upgrade safety.");
        }
        Console.WriteLine("PASS: Explicit quiet/restart matrix for Windows 10/11 and WIM/ESD/SWM; unchanged default switches and image binding. No Setup launched.");
    }
}
