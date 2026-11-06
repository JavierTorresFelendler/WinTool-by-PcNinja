using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WinTool.Upgrade;

public sealed record DownloadSystem(string Edition, string Language, int Build, int Revision,
    string Architecture, string InstallationType, bool IsLtsc = false)
{
    public bool IsServer => InstallationType.Equals("Server", StringComparison.OrdinalIgnoreCase) ||
        InstallationType.Equals("Server Core", StringComparison.OrdinalIgnoreCase);

    public bool IsSupportedClient => InstallationType.Equals("Client", StringComparison.OrdinalIgnoreCase) &&
        IsX64 && !IsLtsc && !DownloadSourcePolicy.IsLtscEdition(Edition) &&
        DownloadSourcePolicy.IsReleasedClientBuild(Build);

    public bool IsX64 => Architecture.Equals("x64", StringComparison.OrdinalIgnoreCase) ||
        Architecture.Equals("amd64", StringComparison.OrdinalIgnoreCase);

    public static DownloadSystem Read()
    {
        var current = CurrentWindows.Read();
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        var product = key?.GetValue("ProductName")?.ToString() ?? "";
        var composition = key?.GetValue("CompositionEditionID")?.ToString() ?? "";
        var revision = key?.GetValue("UBR") is int ubr && ubr >= 0 ? ubr : 0;
        var installationType = key?.GetValue("InstallationType")?.ToString() ?? "";
        // Preserve the execution gate's historical ProductName defense as well
        // as InstallationType/edition detection; never classify Server as client.
        if (product.Contains("Server", StringComparison.OrdinalIgnoreCase) &&
            !installationType.Equals("Server Core", StringComparison.OrdinalIgnoreCase)) installationType = "Server";
        return new(current.Edition, current.Language, current.Build, revision,
            RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
            installationType,
            DownloadSourcePolicy.IsLtscEdition(current.Edition) || DownloadSourcePolicy.IsLtscEdition(composition) ||
                product.Contains("LTSC", StringComparison.OrdinalIgnoreCase) || product.Contains("LTSB", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record PcNinjaIsoSource(string Id, string Name, string Language, string Architecture,
    string BrowserPage, IReadOnlyList<string> Editions);

public sealed record DownloadRecommendation(string Source, string PageUrl, string? FileName,
    string? TargetRelease, string? TargetVersion, bool ExactMatch, string Message);

public static class DownloadSourcePolicy
{
    public const string PcNinjaCatalogPage = "https://iso.pcninja.pro";
    public const string OemPage = "https://winoemiso.pcninja.pro/";
    public const string MicrosoftServerDownloadPage = "https://www.microsoft.com/evalcenter/";

    internal static IReadOnlyList<string> ConsumerEditions { get; } = Array.AsReadOnly(new[]
    {
        "Core", "CoreN", "CoreSingleLanguage", "Education", "EducationN", "Professional", "ProfessionalN",
        "ProfessionalEducation", "ProfessionalEducationN", "ProfessionalWorkstation", "ProfessionalWorkstationN"
    });

    // Priority is explicit and extensible. These public pages are not transport/download APIs.
    public static IReadOnlyList<PcNinjaIsoSource> Sources { get; } = Array.AsReadOnly(new[]
    {
        new PcNinjaIsoSource("pcninja-oem", "PcNinja Windows OEM", "en-US", "x64", OemPage,
            ConsumerEditions)
    });

    // Source coverage permits a download attempt, never permission to run Setup.
    // Do not infer target version from this mutable URL. Final media checks stay mandatory.
    public static PcNinjaIsoSource? GetAutomaticSource(DownloadSystem system) =>
        ManualUpgradePolicy.GetUnavailableReason(system) is not null || system.IsLtsc || IsLtscEdition(system.Edition) ? null :
        Sources.FirstOrDefault(source => source.Language.Equals(system.Language, StringComparison.OrdinalIgnoreCase) &&
            (source.Architecture.Equals(system.Architecture, StringComparison.OrdinalIgnoreCase) ||
                source.Architecture == "x64" && system.IsX64) &&
            source.Editions.Contains(system.Edition, StringComparer.OrdinalIgnoreCase));

    public static string GetPcNinjaBrowserPage(DownloadSystem system) =>
        GetAutomaticSource(system)?.BrowserPage ?? PcNinjaCatalogPage;

    public static string GetMicrosoftBrowserPage(DownloadSystem system) =>
        system.IsServer ? MicrosoftServerDownloadPage : UpgradeEngine.MicrosoftDownloadPage;

    public static string? GetUnavailableReason(DownloadSystem system)
    {
        if (GetAutomaticSource(system) is not null) return null;
        var detail = system.IsServer ? "Windows Server requires matching Server media and a separate supported upgrade decision." :
            !system.IsX64 ? $"The automatic PcNinja source is x64; this system is {system.Architecture}." :
            system.IsLtsc || IsLtscEdition(system.Edition) ? "Automatic transfer is unavailable for LTSC/LTSB. Select matching LTSC media manually, with the exact edition and system UI language." :
            ManualUpgradePolicy.GetUnavailableReason(system) is { } hostReason ? hostReason :
            $"No automatic PcNinja source matches {system.Edition}, {system.Language}, {system.Architecture}.";
        return detail + " Configured automatic media: en-US, x64. Use the matching public ISO recommendation or select matching existing media. " +
            "The downloaded image must still pass signature, image, language, storage and answer-file safety checks.";
    }

    internal static bool IsLtscEdition(string edition) => edition.Equals("EnterpriseS", StringComparison.OrdinalIgnoreCase) ||
        edition.Equals("EnterpriseSN", StringComparison.OrdinalIgnoreCase) ||
        edition.Equals("IoTEnterpriseS", StringComparison.OrdinalIgnoreCase) ||
        edition.Equals("IoTEnterpriseSK", StringComparison.OrdinalIgnoreCase);

    // Explicit released feature families: never treat an arbitrary large/Insider build as eligible.
    // 28000 (26H1) is intentionally a separate repair-only catalog family, not the next update.
    internal static bool IsReleasedClientBuild(int build) => build is 10240 or 10586 or 14393 or 15063 or 16299 or
        17134 or 17763 or 18362 or 18363 or 19041 or 19042 or 19043 or 19044 or 19045 or
        22000 or 22621 or 22631 or 26100 or 26200 or 26300;
}
