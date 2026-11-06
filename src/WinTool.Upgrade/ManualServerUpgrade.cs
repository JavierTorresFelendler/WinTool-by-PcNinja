using System.Globalization;

namespace WinTool.Upgrade;

/// <summary>
/// Builds advisory text only. This class never starts Setup, changes clipboard
/// contents, grants restart consent or enables the client's upgrade operation.
/// The caller must freshly validate the exact source before offering a command.
/// </summary>
public static class ManualServerUpgrade
{
    // Profile/restrictions reviewed against Microsoft Learn:
    // windows-hardware/manufacture/desktop/windows-setup-command-line-options
    // windows-server/get-started/install-upgrade-migrate
    // These metadata gates do not validate roles, clusters, licensing or Setup compatibility.
    public const string Advisory = "Manual command only; matching image metadata is not a compatibility guarantee. " +
        "Verify the supported Windows Server upgrade path, roles/features, backups and a valid target license. " +
        "Run from the current Windows installation in an elevated Command Prompt. " +
        "Proceed only if Windows Setup keeps apps, settings and files; otherwise stop. " +
        "/noreboot suppresses only the first restart; later restarts can occur. PcNinja does not run this command.";

    // Recognized released LTSC Server families only. A larger arbitrary build is
    // not evidence of a supported Server release or upgrade path.
    private static readonly HashSet<int> ReleasedBuilds = [14393, 17763, 20348, 26100];

    public static string? GetIncompatibility(InstallationImage image, DownloadSystem current)
    {
        if (!current.IsServer || NormalizeInstallationType(current.InstallationType) is null)
            return "Manual Server commands require a recognized Windows Server installation.";
        if (!IsSupportedEdition(current.Edition))
            return "Only matching non-evaluation Windows Server Standard or Datacenter editions are supported for manual guidance; evaluation and other editions are excluded.";
        if (!IsSupportedEdition(image.Edition))
            return "Choose licensed Windows Server Standard or Datacenter media. Evaluation and other Server editions are not eligible for this manual command.";
        if (!string.Equals(image.Edition, current.Edition, StringComparison.OrdinalIgnoreCase))
            return "The Server edition does not match this Windows installation.";
        if (!IsX64(current.Architecture) || !IsX64(image.Architecture))
            return "The installed Server and selected image must both be x64.";
        if (string.IsNullOrWhiteSpace(current.Language) || string.IsNullOrWhiteSpace(image.Language) ||
            !string.Equals(image.Language, current.Language, StringComparison.OrdinalIgnoreCase))
            return "Choose Server media with the same system UI language as this Windows installation.";
        var installationType = NormalizeInstallationType(image.InstallationType);
        if (installationType is null)
            return "The selected image is not a recognized Server Core or Server with Desktop Experience image.";
        if (installationType != NormalizeInstallationType(current.InstallationType))
            return "Server Core and Server with Desktop Experience cannot be switched during an in-place upgrade.";
        if (!ReleasedBuilds.Contains(current.Build) ||
            !Version.TryParse(image.Version, out var version) || version.Major != 10 || version.Minor != 0 ||
            !ReleasedBuilds.Contains(version.Build))
            return "Manual guidance recognizes Windows Server 2016, 2019, 2022 and 2025 only; preview and unknown releases are excluded.";
        // Revision is deliberately not a servicing/compatibility guarantee. Setup
        // and the administrator must decide whether same-release repair is allowed.
        if (version.Build < current.Build)
            return "Choose an equal-or-newer recognized Windows Server build; downgrades are not offered.";
        if (image.Index <= 0)
            return "Select a real validated Server image index before copying a command.";
        return null;
    }

    public static string BuildCommand(MountedSource source, InstallationImage image, DownloadSystem current)
    {
        if (GetIncompatibility(image, current) is string reason)
            throw new InvalidOperationException(reason);
        if (source.Images is null || !source.Images.Contains(image) ||
            source.Images.Count(item => item.Index == image.Index) != 1 || source.ImagePath is null)
            throw new InvalidOperationException("Select an image from this exact validated installation source before copying a command.");

        var root = SafeAbsolutePath(source.Root);
        var setup = SafeAbsolutePath(source.SetupPath);
        var imagePath = SafeAbsolutePath(source.ImagePath);
        if (!Same(setup, Path.Combine(root, "setup.exe")) ||
            !Same(Path.GetDirectoryName(imagePath)!, Path.Combine(root, "sources")) ||
            !new[] { "install.wim", "install.esd", "install.swm" }.Contains(Path.GetFileName(imagePath), StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("The validated setup.exe and installation image must belong to the selected source folder.");

        // Intended for an elevated Command Prompt, not PowerShell. Keep Setup's
        // compatibility UX; do not add /quiet, warning bypasses or /product server.
        return $"\"{setup}\" /auto upgrade /noreboot /imageindex {image.Index.ToString(CultureInfo.InvariantCulture)} /installfrom \"{imagePath}\"";
    }

    private static bool IsSupportedEdition(string edition) =>
        string.Equals(edition, "ServerStandard", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(edition, "ServerDatacenter", StringComparison.OrdinalIgnoreCase);

    private static bool IsX64(string architecture) =>
        string.Equals(architecture, "x64", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(architecture, "amd64", StringComparison.OrdinalIgnoreCase);

    private static string? NormalizeInstallationType(string installationType) => installationType.Trim().ToLowerInvariant() switch
    {
        "server" => "Desktop",
        "server core" => "Core",
        _ => null
    };

    private static string SafeAbsolutePath(string path)
    {
        // Quoting does not stop CMD percent expansion or delayed ! expansion.
        // Reject unsupported forms rather than producing a fragile escape recipe.
        if (string.IsNullOrWhiteSpace(path) || path != path.Trim() ||
            path.Any(char.IsControl) || path.IndexOfAny(['"', '%', '!', '^', '<', '>', '|', '*', '?']) >= 0 ||
            path.StartsWith(@"\\.\", StringComparison.Ordinal) || path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            !Fx.IsPathFullyQualified(path) || path.IndexOf(':', 2) >= 0)
            throw new InvalidOperationException("The manual command requires absolute source paths without CMD expansion, control or wildcard characters. Move the media to a simple path and validate again.");
        return Path.GetFullPath(path);
    }

    private static bool Same(string left, string right) =>
        string.Equals(Fx.TrimEndingDirectorySeparator(left), Fx.TrimEndingDirectorySeparator(right), StringComparison.OrdinalIgnoreCase);
}
