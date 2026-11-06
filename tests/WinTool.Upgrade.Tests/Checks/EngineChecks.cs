// Ported from Win-Upgrade-Tool-by-PcNinja tests/PcNinja.Win11Upgrade.SmokeTests/Program.cs (see docs/UPSTREAM-SYNC.md).
using WinTool.Upgrade;

namespace WinTool.Upgrade.Tests.Checks;

internal static class EngineChecks
{
    private static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

    public static void Run()
    {
        var failures = new List<string>();
        var engine = new UpgradeEngine();
        if (!string.Equals(UpgradeEngine.PcNinjaBrowserFallbackPage, "https://iso.pcninja.pro", StringComparison.Ordinal))
            failures.Add("Manual PcNinja downloads must use the public ISO catalog.");
        if (UpgradeEngine.RequiredDownloadBytes(7L * 1024 * 1024 * 1024, true) != 28L * 1024 * 1024 * 1024)
            failures.Add("Combined ISO and Windows workspace reservation failed.");
        if (UpgradeEngine.IsEligibleDownloadLocation("FAT32", long.MaxValue) ||
            UpgradeEngine.IsEligibleDownloadLocation("NTFS", 70L * 1024 * 1024) ||
            !UpgradeEngine.IsEligibleDownloadLocation("exFAT", 16L * 1024 * 1024 * 1024))
            failures.Add("Download drive filtering failed.");
        if (UpgradeEngine.PcNinjaBrowserFallbackPage.Contains("download=1", StringComparison.OrdinalIgnoreCase))
            failures.Add("The browser fallback must not use direct-download mode.");
        if (!Uri.TryCreate(UpgradeEngine.PcNinjaBrowserFallbackPage, UriKind.Absolute, out var fallbackUri) ||
            fallbackUri.Scheme != Uri.UriSchemeHttps)
            failures.Add("The PcNinja browser fallback is not a valid HTTPS URL.");
        // Everything below reads this machine (registry, signed system files, Windows paths).
        if (!IsWindows) { Require(failures); return; }
        var os = engine.GetOperatingSystemDescription();
        if (string.IsNullOrWhiteSpace(os) || !os.Contains("build", StringComparison.OrdinalIgnoreCase))
            failures.Add("Operating system description was not populated.");

        var normalizedWindows11 = UpgradeEngine.FormatOperatingSystemDescription(
            "Windows 10 Pro", "23H2", 22631, true, "Professional");
        if (!normalizedWindows11.StartsWith("Windows 11 Pro 23H2", StringComparison.Ordinal))
            failures.Add($"Windows 11 build normalization failed: {normalizedWindows11}");

        var normalizedWindows10 = UpgradeEngine.FormatOperatingSystemDescription(
            "Windows 10 Home", "22H2", 19045, true, "Core");
        if (!normalizedWindows10.StartsWith("Windows 10 Home 22H2", StringComparison.Ordinal))
            failures.Add($"Windows 10 build normalization failed: {normalizedWindows10}");

        var normalizedServer = UpgradeEngine.FormatOperatingSystemDescription(
            "Windows Server 2022 Datacenter", "21H2", 20348, true);
        if (!normalizedServer.StartsWith("Windows Server 2022 Datacenter 21H2", StringComparison.Ordinal))
            failures.Add($"Windows Server reporting failed: {normalizedServer}");

        var microsoftFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "curl.exe");
        if (!AuthenticodeVerifier.IsTrustedMicrosoftFile(microsoftFile, out var signer))
            failures.Add($"Microsoft signature validation rejected {microsoftFile}. Signer: {signer}");
        else if (!signer.Contains("Microsoft Corporation", StringComparison.OrdinalIgnoreCase))
            failures.Add("The accepted Microsoft file returned an unexpected signer.");

        var unsignedFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(unsignedFile, "not a signed executable");
            if (AuthenticodeVerifier.IsTrustedMicrosoftFile(unsignedFile, out _))
                failures.Add("Signature validation accepted an unsigned file.");
        }
        finally
        {
            File.Delete(unsignedFile);
        }

        var extractedFolder = Path.Combine(Path.GetTempPath(), $"PcNinja-Extracted-{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractedFolder);
        try
        {
            var extractedSetup = Path.Combine(extractedFolder, "setup.exe");
            File.Copy(microsoftFile, extractedSetup);
            var extractedSource = engine.ValidateMountedSource(extractedFolder);
            if (!string.Equals(extractedSource.Root, Path.GetFullPath(extractedFolder), StringComparison.OrdinalIgnoreCase))
                failures.Add($"Extracted-folder root was changed unexpectedly: {extractedSource.Root}");
            if (!string.Equals(extractedSource.SetupPath, extractedSetup, StringComparison.OrdinalIgnoreCase))
                failures.Add($"Extracted-folder setup path was not preserved: {extractedSource.SetupPath}");
            Directory.CreateDirectory(Path.Combine(extractedFolder, "sources"));
            File.WriteAllText(Path.Combine(extractedFolder, "sources", "install.wim"), "not launched - safety fixture only");
            var lateAnswer = Path.Combine(extractedFolder, "Autounattend.xml");
            File.WriteAllText(lateAnswer, "late answer file");
            try
            {
                engine.ValidateMountedSource(extractedFolder, removeSelectedAnswerFiles: false);
                failures.Add("Pre-launch source validation allowed a late answer file.");
            }
            catch (UnsafeInstallationMediaException) { }
            if (!File.Exists(lateAnswer)) failures.Add("Pre-launch validation silently deleted a late answer file.");
            engine.ValidateMountedSource(extractedFolder);
            if (File.Exists(lateAnswer)) failures.Add("Preparation did not clean the selected writable answer file.");
        }
        finally
        {
            Directory.Delete(extractedFolder, true);
        }
        Require(failures);
    }

    private static void Require(List<string> failures)
    {
        if (failures.Count > 0) throw new Exception(string.Join(Environment.NewLine, failures));
    }
}
