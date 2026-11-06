namespace WinTool.Upgrade;

public sealed record MountedSource(string Root, string SetupPath, string? ImagePath = null,
    IReadOnlyList<InstallationImage>? Images = null);

public sealed record IsoPreparationResult(MountedSource Source, string Sha256, bool ReusedValidation = false);

public sealed record UpgradeResult(int ExitCode, string SetupPath)
{
    public const string ManualRestartMessage =
        "Upgrade preparation is complete. Restart your computer to continue the upgrade.";

    public bool NeedsManualRestartNotice(bool allowAutomaticRestart) => ExitCode == 0 && !allowAutomaticRestart;
}

public sealed record DownloadProgress(
    string DestinationPath,
    long BytesReceived,
    long? TotalBytes,
    int? Percentage,
    string? Stage = null);

public sealed record DownloadResult(
    string IsoPath,
    long BytesReceived,
    long? ExpectedBytes,
    string? Sha256 = null,
    bool Resumed = false,
    bool Reused = false,
    MountedSource? ValidatedSource = null);

public sealed record StorageCheck(
    bool IsValid,
    string RootPath,
    string FileSystem,
    long AvailableBytes,
    long RequiredBytes,
    string Message);

public sealed record StorageLocation(
    string RootPath,
    string VolumeLabel,
    DriveType DriveType,
    string FileSystem,
    long AvailableBytes)
{
    public override string ToString()
    {
        var label = string.IsNullOrWhiteSpace(VolumeLabel) ? "Local disk" : VolumeLabel;
        return $"{RootPath}  {label} - {UpgradeEngine.FormatBytes(AvailableBytes)} free ({FileSystem})";
    }
}
