// Ported from Win-Upgrade-Tool-by-PcNinja tests/PcNinja.Win11Upgrade.SmokeTests (see docs/UPSTREAM-SYNC.md).
using WinTool.Upgrade;

namespace WinTool.Upgrade.Tests.Checks;

internal static class ManualServerUpgradeChecks
{
    internal static void Run()
    {
        // Pure metadata/path fixtures only; no files, clipboard writes, processes,
        // mounted media or Windows Setup are needed to build this advisory text.
        var current = new DownloadSystem("ServerStandard", "en-US", 17763, 9000, "x64", "Server");
        var image = new InstallationImage(2, "Windows Server Standard", "ServerStandard", "amd64",
            "10.0.26100.1", "Server", "en-US");
        var source = Source(@"E:\", image);
        var command = ManualServerUpgrade.BuildCommand(source, image, current);
        if (command != "\"E:\\setup.exe\" /auto upgrade /noreboot /imageindex 2 /installfrom \"E:\\sources\\install.wim\"")
            throw new Exception("The manual Server command did not preserve the exact validated source/index and safe profile.");
        foreach (var forbidden in new[] { "/product", "/quiet", "/compat", "/auto clean", "/auto dataonly", "shutdown", "/eula" })
            if (command.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                throw new Exception("The advisory Server command contains an automatic, destructive or warning-suppressing option.");

        foreach (var build in new[] { 14393, 17763, 20348, 26100 })
        {
            var released = image with { Version = $"10.0.{build}.1" };
            var installed = current with { Build = build };
            RequireCompatible(released, installed); // Same-release guidance is not a repair guarantee.
            RequireCompatible(image, installed);
        }
        RequireCompatible(image with { Edition = "ServerDatacenter" }, current with { Edition = "ServerDatacenter" });
        RequireCompatible(image with { InstallationType = "server core", Architecture = "x64", Language = "EN-us" },
            current with { InstallationType = "Server Core", Architecture = "amd64" });

        foreach (var rejectedImage in new[]
        {
            image with { Edition = "ServerDatacenter" },
            image with { Edition = "ServerStandardEval" },
            image with { Edition = "ServerAzureStackHCI" },
            image with { Edition = "ServerStorageStandard" },
            image with { Architecture = "arm64" },
            image with { Language = "en-GB" },
            image with { Language = "" },
            image with { InstallationType = "Client" },
            image with { InstallationType = "Server Core" },
            image with { InstallationType = "ServerUnknown" },
            image with { Version = "10.0.26300.1" },
            image with { Version = "11.0.26100.1" },
            image with { Version = "invalid" },
            image with { Index = 0 },
            image with { Index = -1 }
        }) RequireRejected(rejectedImage, current);
        RequireRejected(image with { Version = "10.0.17763.1" }, current with { Build = 20348 });
        foreach (var rejectedSystem in new[]
        {
            current with { Edition = "Professional", InstallationType = "Client" },
            current with { Edition = "ServerStandardEval" },
            current with { Edition = "ServerUnknown" },
            current with { Architecture = "x86" },
            current with { Language = "" },
            current with { InstallationType = "Server Core" },
            current with { InstallationType = "Unknown" },
            current with { Build = 7601 },
            current with { Build = 26300 }
        }) RequireRejected(image, rejectedSystem);

        ExpectBlocked(() => ManualServerUpgrade.BuildCommand(source with { ImagePath = null }, image, current));
        ExpectBlocked(() => ManualServerUpgrade.BuildCommand(source with { Images = null }, image, current));
        ExpectBlocked(() => ManualServerUpgrade.BuildCommand(source with { Images = [] }, image, current));
        ExpectBlocked(() => ManualServerUpgrade.BuildCommand(source, image with { Name = "Not the validated image" }, current));
        ExpectBlocked(() => ManualServerUpgrade.BuildCommand(source with { Images = [image, image] }, image, current));
        ExpectBlocked(() => ManualServerUpgrade.BuildCommand(source with { SetupPath = @"F:\setup.exe" }, image, current));
        ExpectBlocked(() => ManualServerUpgrade.BuildCommand(source with { ImagePath = @"F:\sources\install.wim" }, image, current));
        ExpectBlocked(() => ManualServerUpgrade.BuildCommand(source with { SetupPath = @"E:\other.exe" }, image, current));
        ExpectBlocked(() => ManualServerUpgrade.BuildCommand(source with { ImagePath = @"E:\sources\other.wim" }, image, current));

        foreach (var unsafeRoot in new[]
        {
            "relative", @"C:relative", @"\root-relative", @"https:\server\media", "",
            @"C:\bad\" + '"', @"C:\%TEMP%\media", @"C:\!name!\media", @"C:\bad^path",
            "C:\\bad\r\npath", "C:\\bad\tpath", @"C:\<media>", @"C:\wild*card", @"C:\bad|pipe",
            @"\\?\C:\media", @"\\.\C:\media", @"C:\media:stream", @"C:\media "
        }) ExpectBlocked(() => ManualServerUpgrade.BuildCommand(Source(unsafeRoot, image), image, current));
        foreach (var validRoot in new[] { @"C:\Server media & tools (test)\עברית", @"\\fixture-server\share\Server media" })
        {
            var exactSource = Source(validRoot, image);
            var exactCommand = ManualServerUpgrade.BuildCommand(exactSource, image, current);
            if (!exactCommand.StartsWith('"' + exactSource.SetupPath + '"', StringComparison.Ordinal) ||
                !exactCommand.EndsWith('"' + exactSource.ImagePath + '"', StringComparison.Ordinal))
                throw new Exception("The manual command failed to quote exact Unicode/network/spaced source paths.");
        }
        foreach (var fileName in new[] { "install.wim", "install.esd", "install.swm" })
            ManualServerUpgrade.BuildCommand(source with { ImagePath = @"E:\sources\" + fileName }, image, current);
        foreach (var detail in new[] { "not a compatibility guarantee", "roles/features", "backups", "license", "keeps apps", "first restart", "does not run" })
            if (!ManualServerUpgrade.Advisory.Contains(detail, StringComparison.OrdinalIgnoreCase))
                throw new Exception("The manual Server advisory omitted a safety/compatibility/restart limitation.");
        Console.WriteLine("PASS: Manual-only Server command metadata/build/type/edition/language/x64 guards, exact validated index/source, CMD path safety and advisory. No Setup or clipboard used.");
    }

    private static MountedSource Source(string root, InstallationImage image) =>
        new(root, Path.Combine(root, "setup.exe"), Path.Combine(root, "sources", "install.wim"), [image]);

    private static void RequireCompatible(InstallationImage image, DownloadSystem current)
    {
        if (ManualServerUpgrade.GetIncompatibility(image, current) is string reason)
            throw new Exception("Matching released Server metadata was rejected: " + reason);
    }

    private static void RequireRejected(InstallationImage image, DownloadSystem current)
    {
        if (ManualServerUpgrade.GetIncompatibility(image, current) is null)
            throw new Exception("Mismatched/unknown Server metadata was accepted for a manual command.");
        ExpectBlocked(() => ManualServerUpgrade.BuildCommand(Source(@"E:\", image), image, current));
    }

    private static void ExpectBlocked(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or NotSupportedException) { return; }
        throw new Exception("An invalid Server command/source/path was not blocked.");
    }
}
