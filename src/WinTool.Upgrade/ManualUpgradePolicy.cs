namespace WinTool.Upgrade;

/// <summary>Host eligibility only; actual image, storage and final safety checks remain mandatory.</summary>
public static class ManualUpgradePolicy
{
    public static string? GetUnavailableReason(DownloadSystem system)
    {
        if (system.IsServer || system.Edition.StartsWith("Server", StringComparison.OrdinalIgnoreCase))
            return "Windows Server is for download and manual command guidance only. This assistant will not run Server Setup.";
        if (!system.IsX64) return "This release supports x64 computers only.";
        if (!system.InstallationType.Equals("Client", StringComparison.OrdinalIgnoreCase))
            return "This Windows installation type is not supported for an in-place client upgrade.";
        if (system.Build < 10240 || system.Build is > 19045 and < 22000)
            return "Manual upgrades require a Windows 10 or Windows 11 client installation.";
        // LTSC needs exact edition/language media, not a blanket host ban. It
        // remains ineligible for the configured automatic consumer download.
        return null;
    }

    public static void EnsureAvailable(DownloadSystem system)
    {
        if (GetUnavailableReason(system) is { } reason) throw new InvalidOperationException(reason);
    }
}
