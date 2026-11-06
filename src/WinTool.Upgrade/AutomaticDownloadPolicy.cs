namespace WinTool.Upgrade;

public static class AutomaticDownloadPolicy
{
    // This describes the configured automatic ISO, not the application's UI language.
    public const string ImageLanguage = "en-US";

    public static bool IsAvailable(string? systemLanguage) =>
        string.Equals(systemLanguage, ImageLanguage, StringComparison.OrdinalIgnoreCase);

    public static bool IsAvailable(DownloadSystem system) => DownloadSourcePolicy.GetAutomaticSource(system) is not null;

    public static string? GetUnavailableReason(DownloadSystem system) =>
        IsAvailable(system) ? null : DownloadSourcePolicy.GetUnavailableReason(system);

    public static void EnsureAvailable(DownloadSystem system)
    {
        if (GetUnavailableReason(system) is string reason) throw new InvalidOperationException(reason);
    }

    public static string? GetUnavailableReason(string? systemLanguage) => IsAvailable(systemLanguage) ? null :
        $"Automatic download is unavailable for system language {DisplayLanguage(systemLanguage)}. " +
        $"The automatic ISO is {ImageLanguage}. Download matching Windows 11 media from " +
        $"{UpgradeEngine.PcNinjaBrowserFallbackPage} or {UpgradeEngine.MicrosoftDownloadPage}, " +
        "then select the exact ISO or setup folder. For standard Microsoft media, prefer Microsoft.";

    public static void EnsureAvailable(string? systemLanguage)
    {
        if (GetUnavailableReason(systemLanguage) is string reason)
            throw new InvalidOperationException(reason);
    }

    private static string DisplayLanguage(string? language) =>
        string.IsNullOrWhiteSpace(language) ? "unknown" : language;
}
