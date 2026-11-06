// Ported from Win-Upgrade-Tool-by-PcNinja tests/PcNinja.Win11Upgrade.SmokeTests (see docs/UPSTREAM-SYNC.md).
using WinTool.Upgrade;

namespace WinTool.Upgrade.Tests.Checks;

internal static class Windows10TargetChecks
{
    internal static void Run()
    {
        var windows10 = new InstallationImage(2, "Windows 10 Pro", "Professional", "x64", "10.0.19041", "Client", "en-US");
        var current = new CurrentWindows("Professional", "en-US", 19043);
        var source = new MountedSource("X:\\", "X:\\setup.exe", "X:\\sources\\install.wim", [windows10]);
        foreach (var hostBuild in new[] { 10240, 19041, 19043, 19045 })
            foreach (var imageBuild in new[] { 10240, 19041, 19043, 19045 })
            {
                var image = windows10 with { Version = $"10.0.{imageBuild}" };
                Require(InstallationImages.Select(source with { Images = [image] }, 2, current with { Build = hostBuild }) == image,
                    "Windows 10 POC unexpectedly compared older/equal/newer build order.");
            }
        Require(InstallationImages.GetSetupProfile(windows10) == WindowsSetupProfile.Windows10,
            "Windows 10 base image was not identified without trusting its name.");
        Require(InstallationImages.GetSetupProfile(windows10 with { Name = "Windows 11 misleading name" }) == WindowsSetupProfile.Windows10,
            "Profile was selected from a display name instead of image metadata.");
        foreach (var bad in new[]
        {
            windows10 with { Version = "6.3.9600" }, windows10 with { Version = "10.0.20348" },
            windows10 with { Version = "11.0.19045" }, windows10 with { Version = "not-a-version" },
            windows10 with { InstallationType = "Server" }, windows10 with { Architecture = "x86" },
            windows10 with { Language = "en-GB" }, windows10 with { Edition = "Core" }
        })
            Reject(() => InstallationImages.Select(source with { Images = [bad] }, 2, current));
        foreach (var build in new[] { 9600, 20348, 22000, 22631, 26300 })
            Reject(() => InstallationImages.Select(source, 2, current with { Build = build }));
        Reject(() => InstallationImages.Select(source, 99, current));
        var otherWindows10 = windows10 with { Index = 3, Version = "10.0.19045" };
        var multipleWindows10 = source with { Images = [windows10, otherWindows10] };
        Reject(() => InstallationImages.Select(multipleWindows10, null, current));
        Require(InstallationImages.Select(multipleWindows10, 3, current) == otherWindows10,
            "Windows 10 image selector guessed a version instead of honoring the exact index.");
        foreach (var language in new[] { "en-GB", "he-IL", "ru-RU" })
        {
            var localImage = windows10 with { Language = language };
            Require(InstallationImages.Select(source with { Images = [localImage] }, 2, current with { Language = language }) == localImage,
                "Matching manually supplied Windows 10 language was rejected.");
        }

        foreach (var imageFile in new[] { "install.wim", "install.esd", "install.swm" })
            foreach (var restart in new[] { false, true })
            {
                var arguments = UpgradeEngine.BuildSetupArguments(source with { ImagePath = "X:\\sources\\" + imageFile }, windows10, restart);
                Require(arguments.StartsWith("/auto upgrade /dynamicupdate disable /compat ignorewarning /imageindex 2 /installfrom "),
                    "Windows 10 command prefix/index changed.");
                Require(arguments.Contains(imageFile + "\"") && arguments.Contains("/noreboot") == !restart,
                    "Windows 10 source/restart binding changed.");
                foreach (var forbidden in new[] { "/product", "/eula", "/quiet", "/auto clean", "/auto dataonly", "/unattend" })
                    Require(!arguments.Contains(forbidden, StringComparison.OrdinalIgnoreCase), "Unexpected Windows 10 Setup switch.");
            }
        Reject(() => UpgradeEngine.BuildSetupArguments(source with { Images = [] }, windows10, true));
        var unknown = windows10 with { Version = "10.0.20348" };
        Reject(() => UpgradeEngine.BuildSetupArguments(source with { Images = [unknown] }, unknown, true));

        var windows11 = windows10 with { Name = "Windows 11 Pro", Version = "10.0.26300" };
        var windows11Source = source with { Images = [windows11] };
        var windows11Args = UpgradeEngine.BuildSetupArguments(windows11Source, windows11, false);
        Require(windows11Args == "/product server /auto upgrade /eula accept /dynamicupdate disable /compat ignorewarning" +
            " /imageindex 2 /installfrom \"X:\\sources\\install.wim\" /noreboot", "Windows 11 command profile regressed.");
        Require(InstallationImages.Select(windows11Source, null, current) == windows11, "Windows 10 -> 11 regressed.");
        Require(InstallationImages.Select(windows11Source, null, current with { Build = 26300 }) == windows11,
            "Equal-build Windows 11 repair was rejected.");
        Reject(() => InstallationImages.Select(windows11Source, 2, current with { Build = 99999 }));
        var olderWindows11 = windows11 with { Version = "10.0.22621" };
        Reject(() => InstallationImages.Select(source with { Images = [olderWindows11] }, 2, current with { Build = 22631 }));
        InstallationImages.EnsureAutomaticWindows11Source(windows11Source);
        Reject(() => InstallationImages.EnsureAutomaticWindows11Source(source));
        Reject(() => InstallationImages.EnsureAutomaticWindows11Source(source with { Images = [windows10, windows11] }));

        var attempt = new UpgradeAttempt(DateTimeOffset.UtcNow, "X:\\", source.ImagePath!, 2,
            19045, 19041, "Professional", 0, "PreparationCompleted", "pending manual verification");
        Require(UpgradeHistory.Reconcile(attempt, current with { Build = 19041 }, false).State == "PreparationCompleted",
            "Windows 10 shared-base metadata was treated as final migration proof.");
        Console.WriteLine("PASS: Windows 10 manual-image profile with no intra-family build-order check; edition/language/x64 and cross-family blocks; WIM/ESD/SWM args; automatic Windows 11-only; Windows 11 regression; honest outcome status. No Setup launched.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("An incompatible image or command was unexpectedly accepted.");
    }
}
