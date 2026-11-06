// Ported from Win-Upgrade-Tool-by-PcNinja tests/PcNinja.Win11Upgrade.SmokeTests (see docs/UPSTREAM-SYNC.md).
using WinTool.Upgrade;

namespace WinTool.Upgrade.Tests.Checks;

internal static class ManualUpgradeChecks
{
    internal static void Run()
    {
        foreach (var edition in new[] { "EnterpriseS", "EnterpriseSN", "IoTEnterpriseS", "IoTEnterpriseSK" })
        foreach (var language in new[] { "he-IL", "en-US" })
        {
            var system = new DownloadSystem(edition, language, 19044, 1288, "x64", "Client", true);
            ManualUpgradePolicy.EnsureAvailable(system);
            Require(DownloadSourcePolicy.GetAutomaticSource(system) is null && !AutomaticDownloadPolicy.IsAvailable(system),
                "Manual LTSC eligibility incorrectly enabled the automatic consumer download.");
            var current = new CurrentWindows(edition, language, system.Build);
            var image = new InstallationImage(1, "LTSC fixture", edition, "x64", "10.0.26100.1", "Client", language);
            var source = new MountedSource(@"X:\fixture", @"X:\fixture\setup.exe", @"X:\fixture\sources\install.wim", [image]);
            Require(InstallationImages.Select(source, 1, current) == image, "Matching LTSC image was rejected.");
            foreach (var automaticRestart in new[] { false, true })
            foreach (var quiet in new[] { false, true })
            {
                var command = UpgradeEngine.BuildSetupArguments(source, image, automaticRestart, quiet);
                Require(command.Contains("/product server /auto upgrade") && command.Contains("/imageindex 1 /installfrom") &&
                    command.Contains("/noreboot") == !automaticRestart && command.Contains("/quiet") == quiet &&
                    !command.Contains("/auto clean") && !command.Contains("/auto dataonly"), "LTSC changed upgrade-only command/quiet/restart intent.");
            }
            foreach (var bad in new[] { image with { Edition = "Enterprise" }, image with { Edition = "Professional" },
                image with { Language = language == "he-IL" ? "en-US" : "he-IL" }, image with { Architecture = "arm64" },
                image with { InstallationType = "Server" }, image with { Version = "10.0.20348" } })
                Require(InstallationImages.GetIncompatibility(bad, current) is not null, "Manual LTSC weakened an image gate.");
            Require(InstallationImages.GetIncompatibility(image, current with { Edition = "Enterprise" }) is not null,
                "General Availability Enterprise was silently converted to LTSC.");
            var windows10 = image with { Version = "10.0.19041" };
            Require(InstallationImages.Select(source with { Images = [windows10] }, 1, current) == windows10 &&
                !UpgradeEngine.BuildSetupArguments(source with { Images = [windows10] }, windows10, false).Contains("/product server"),
                "Existing Windows 10 target POC profile was changed for LTSC.");
            Require(InstallationImages.GetIncompatibility(windows10, current with { Build = 26100 }) is not null,
                "Windows 11 LTSC to Windows 10 was enabled.");
            Require(InstallationImages.GetIncompatibility(image with { Version = "10.0.22000" }, current with { Build = 26100 }) is not null,
                "Older Windows 11 LTSC target was enabled.");
        }
        var client = new DownloadSystem("Professional", "en-US", 26300, 1, "x64", "Client");
        ManualUpgradePolicy.EnsureAvailable(client);
        foreach (var blocked in new[] { client with { InstallationType = "Server" }, client with { InstallationType = "Server Core" },
            client with { Edition = "ServerStandard" }, client with { InstallationType = "Unknown" },
            client with { Architecture = "arm64" }, client with { Architecture = "x86" }, client with { Build = 9600 },
            client with { Build = 20348 } })
        {
            Require(ManualUpgradePolicy.GetUnavailableReason(blocked) is not null, "Unsupported host became executable.");
            try { ManualUpgradePolicy.EnsureAvailable(blocked); throw new Exception("Host gate did not throw."); }
            catch (InvalidOperationException) { }
        }
        Console.WriteLine("PASS: Shared manual LTSC host eligibility, exact edition/N/IoT/language/image gates, unchanged Windows 10/11 command/restart/quiet profiles, automatic exclusion and Server/architecture/type blocks. No media or Setup used.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
