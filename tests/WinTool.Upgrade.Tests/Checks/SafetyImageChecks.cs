// Ported from Win-Upgrade-Tool-by-PcNinja tests/PcNinja.Win11Upgrade.SmokeTests (see docs/UPSTREAM-SYNC.md).
using WinTool.Upgrade;

namespace WinTool.Upgrade.Tests.Checks;

internal static class SafetyImageChecks
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "PcNinja-Safety-" + Guid.NewGuid().ToString("N"));
        var selected = Path.Combine(root, "selected");
        var secondary = Path.Combine(root, "secondary");
        Directory.CreateDirectory(Path.Combine(selected, "sources"));
        Directory.CreateDirectory(secondary);
        try
        {
            File.WriteAllText(Path.Combine(selected, "setup.exe"), "test placeholder - never launched");
            File.WriteAllText(Path.Combine(selected, "sources", "install.wim"), "test placeholder");
            var answer = Path.Combine(selected, "Autounattend.xml");
            File.WriteAllText(answer, "test answer file");
            File.SetAttributes(answer, FileAttributes.ReadOnly);
            InstallationSafety.Check(selected, [secondary], true);
            Require(!File.Exists(answer), "Writable selected-source answer file was not removed.");

            var outside = Path.Combine(secondary, "Autounattend.xml");
            File.WriteAllText(outside, "outside selection");
            MustBlock(() => InstallationSafety.Check(selected, [secondary], true));
            Require(File.Exists(outside), "A secondary-media file was deleted.");
            File.Delete(outside);
            InstallationSafety.Check(selected, [secondary], false);

            File.WriteAllText(answer, "newly attached after preparation");
            MustBlock(() => InstallationSafety.Check(selected, [secondary], false));
            Require(File.Exists(answer), "Final pre-launch gate removed a newly introduced answer file.");
            using (var held = new FileStream(answer, FileMode.Open, FileAccess.Read, FileShare.Read))
                MustBlock(() => InstallationSafety.Check(selected, [secondary], true));
            File.Delete(answer);
            var unrelated = Path.Combine(selected, "personal.xml");
            File.WriteAllText(unrelated, "keep");
            InstallationSafety.Check(selected, [secondary], true);
            Require(File.Exists(unrelated), "An unrelated file was removed.");

            var detail = """
Deployment Image Servicing and Management tool
Version: 10.0.19041.1

Details for image : example.wim
Index : 6
Name : Windows 11 Pro
Architecture : x64
Version : 10.0.26300
Edition : Professional
Installation : Client
Languages :
    en-US (Default)
    he-IL
""";
            var pro = InstallationImages.ParseDetail(detail, 6);
            Require(pro.Version == "10.0.26300" && pro.Language == "en-US", "DISM tool version was mistaken for the image version.");
            Require(InstallationImages.ParseIndexes("Index : 1\nName : Home\nIndex : 6\nName : Pro").SequenceEqual(new[] { 1, 6 }), "Image index parsing failed.");
            var home = pro with { Index = 1, Name = "Home", Edition = "Core" };
            var source = new MountedSource(selected, Path.Combine(selected, "setup.exe"), Path.Combine(selected, "sources", "install.swm"), [home, pro]);
            var current = new CurrentWindows("Professional", "en-US", 19045);
            Require(InstallationImages.Select(source, null, current) == pro, "Matching edition was not selected.");
            Require(AutomaticDownloadPolicy.IsAvailable("en-US") && AutomaticDownloadPolicy.IsAvailable("EN-us"),
                "The configured automatic ISO language was rejected.");
            foreach (var language in new[] { "he-IL", "en-GB", "ru-RU", "ar-SA", "ja-JP", "de-DE" })
            {
                Require(!AutomaticDownloadPolicy.IsAvailable(language), "A mismatching automatic download was enabled.");
                Reject(() => AutomaticDownloadPolicy.EnsureAvailable(language));
                var reason = AutomaticDownloadPolicy.GetUnavailableReason(language)!;
                Require(reason.Contains(language) && reason.Contains("en-US") && reason.Contains("https://iso.pcninja.pro") &&
                    reason.Contains(UpgradeEngine.MicrosoftDownloadPage), "Language rejection omitted the reason or manual alternatives.");
                var localImage = pro with { Language = language };
                Require(InstallationImages.Select(source with { Images = [localImage] }, null, current with { Language = language }) == localImage,
                    "Matching manually supplied non-US media was rejected because of its language.");
            }
            Require(!AutomaticDownloadPolicy.IsAvailable((string?)null) && !AutomaticDownloadPolicy.IsAvailable(""),
                "An unknown system language enabled automatic download.");
            var mismatch = InstallationImages.GetIncompatibility(pro, current with { Language = "he-IL" })!;
            Require(mismatch.Contains("he-IL") && mismatch.Contains("en-US"), "Image mismatch did not explain both languages.");
            Reject(() => InstallationImages.Select(source, 1, current));
            Reject(() => InstallationImages.Select(source, 99, current));
            Reject(() => InstallationImages.Select(source, 6, current with { Language = "he-IL" }));
            Reject(() => InstallationImages.Select(source, 6, current with { Edition = "ProfessionalN" }));
            Reject(() => InstallationImages.Select(source, 6, current with { Build = 99999 }));
            var duplicate = pro with { Index = 7 };
            Reject(() => InstallationImages.Select(source with { Images = [pro, duplicate] }, null, current));
            Require(InstallationImages.Select(source with { Images = [pro, duplicate] }, 7, current).Index == 7, "Explicit duplicate-name selection failed.");
            var args = UpgradeEngine.BuildSetupArguments(source, pro, false);
            Require(args.Contains("/auto upgrade") && args.Contains("/imageindex 6") && args.Contains("install.swm\"") && args.EndsWith(" /noreboot"), "Setup argument binding failed.");
            Require(!args.Contains("/auto clean") && !args.Contains("/unattend") && !args.Contains("/quiet"), "Unsafe Setup arguments were introduced.");
            Reject(() => UpgradeEngine.BuildSetupArguments(source with { Images = [home] }, pro, true));

            var attempt = new UpgradeAttempt(DateTimeOffset.UtcNow, selected, "install.wim", 6, 19045, 26300,
                "Professional", 0x40010004, "OutcomeUnconfirmed", "unknown");
            Require(UpgradeHistory.Reconcile(attempt, current, false).State == "OutcomeUnconfirmed", "An exit code was treated as upgrade success.");
            Require(UpgradeHistory.Reconcile(attempt, current with { Build = 26300 }, true).State == "OutcomeUnconfirmed", "An active Setup was treated as completed.");
            Require(UpgradeHistory.Reconcile(attempt, current with { Build = 26300 }, false).State == "TargetWindowsObserved", "Post-restart OS evidence was not recognized.");
            Console.WriteLine("PASS: Selected/secondary/locked/late answer-file gates; source-bound images, edition/language/build guards; upgrade-only arguments; evidence-based status. No Setup launched.");
        }
        finally
        {
            foreach (var path in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(root, true);
        }
    }

    private static void MustBlock(Action action)
    {
        try { action(); }
        catch (UnsafeInstallationMediaException) { return; }
        throw new Exception("Unsafe answer-file scenario was allowed.");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("An incompatible/ambiguous/unbound image was accepted.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
