using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace WinTool.Upgrade;

/// <summary>Resolves public browser landing pages only. Never downloads media or automates a CAPTCHA.</summary>
public static class IsoDownloadCatalog
{
    public const string ClientCatalogPage = "https://massgrave.dev/windows_11_links";
    public const string ServerCatalogPage = "https://massgrave.dev/windows-server-links";
    internal const int MaximumCatalogBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);
    private static readonly Regex Tables = Pattern(@"<table\b[^>]*>.*?</table\s*>");
    private static readonly Regex Headings = Pattern(@"<h[1-6]\b[^>]*>.*?</h[1-6]\s*>|<strong\b[^>]*>.*?</strong\s*>");
    private static readonly Regex RowStarts = Pattern(@"<tr\b[^>]*>");
    private static readonly Regex CellStarts = Pattern(@"<t[dh]\b[^>]*>");
    private static readonly Regex Anchors = Pattern(@"<a\b(?<attributes>[^>]*)>(?<text>.*?)</a\s*>");
    private static readonly Regex Hrefs = Pattern("(?:^|\\s)href\\s*=\\s*(?:\"(?<double>[^\"]*)\"|'(?<single>[^']*)'|(?<bare>[^\\s>]+))");
    private static readonly Regex Tags = Pattern(@"<[^>]*>");
    private static readonly Regex Whitespace = Pattern(@"\s+");
    private static readonly Regex RemovedBlocks = Pattern(@"<script\b[^>]*>.*?</script\s*>|<style\b[^>]*>.*?</style\s*>|<!--.*?-->");
    private static readonly Regex BuildText = Pattern(@"\bBuild\s*[-:]\s*(?:10\.0\.)?(?<build>\d{4,5})\.(?<revision>\d{1,8})\b");
    private static readonly Regex ClientTitle = Pattern(@"^Windows\s+11\s+(?<pack>Consumer|Business|IoT Enterprise)\s+(?<release>\d{2}H[12])$");
    private static readonly Regex ServerTitle = Pattern(@"^Windows\s+Server\s+(?<release>20\d{2})$");
    private static readonly Regex ClientFile = Pattern(@"^(?<language>[a-z]{2,3}(?:-[a-z]{2,8}){1,2})_windows_11_(?<pack>consumer_editions|business_editions|iot_enterprise)_version_(?<release>\d{2}h[12])(?:_updated_[a-z]{3,9}_\d{4})?_(?<arch>x64|arm64)_dvd_[a-f0-9]{8}\.iso$");
    private static readonly Regex ServerFile = Pattern(@"^(?<language>[a-z]{2,3}(?:-[a-z]{2,8}){1,2})_windows_server_(?<release>20\d{2})(?:_updated_[a-z]{3,9}_\d{4})?_(?<arch>x64)_dvd_[a-f0-9]{8}\.iso$");
    private static readonly Regex ZeroFsPath = Pattern(@"^/f/[a-z0-9_-]+/?$");
    private static readonly Regex BuzzPath = Pattern(@"^/[a-z0-9]{6,128}/?$");
    private static readonly IReadOnlyDictionary<string, int> ClientBuilds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["21H2"] = 22000, ["22H2"] = 22621, ["23H2"] = 22631, ["24H2"] = 26100,
        ["25H2"] = 26200, ["26H2"] = 26300, ["26H1"] = 28000
    };
    private static readonly IReadOnlyDictionary<string, int> ServerBuilds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["2016"] = 14393, ["2019"] = 17763, ["2022"] = 20348, ["2025"] = 26100
    };

    // Exact culture tags are authoritative; labels independently check table-row alignment.
    internal static IReadOnlyDictionary<string, string> Languages { get; } =
        new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ar-SA"]="Arabic", ["bg-BG"]="Bulgarian", ["cs-CZ"]="Czech", ["da-DK"]="Danish",
            ["de-DE"]="German", ["el-GR"]="Greek", ["en-GB"]="English-United Kingdom", ["en-US"]="English",
            ["es-ES"]="Spanish", ["es-MX"]="Spanish-Mexico", ["et-EE"]="Estonian", ["fi-FI"]="Finnish",
            ["fr-CA"]="French-Canada", ["fr-FR"]="French", ["he-IL"]="Hebrew", ["hr-HR"]="Croatian",
            ["hu-HU"]="Hungarian", ["it-IT"]="Italian", ["ja-JP"]="Japanese", ["ko-KR"]="Korean",
            ["lt-LT"]="Lithuanian", ["lv-LV"]="Latvian", ["nb-NO"]="Norwegian-Bokmal", ["nl-NL"]="Dutch-Netherlands",
            ["pl-PL"]="Polish", ["pt-BR"]="Portuguese-Brazil", ["pt-PT"]="Portuguese-Portugal", ["ro-RO"]="Romanian",
            ["ru-RU"]="Russian", ["sk-SK"]="Slovak", ["sl-SI"]="Slovenian", ["sr-Latn-RS"]="Serbian-Latin",
            ["sv-SE"]="Swedish", ["th-TH"]="Thai", ["tr-TR"]="Turkish", ["uk-UA"]="Ukranian",
            ["zh-CN"]="Chinese-Simplified", ["zh-TW"]="Chinese-Traditional"
        });

    public static async Task<DownloadRecommendation> ResolveAsync(HttpClient http, DownloadSystem system,
        CancellationToken cancellationToken)
    {
        Compat.ThrowIfNull(http, nameof(http));
        cancellationToken.ThrowIfCancellationRequested();
        if (DownloadSourcePolicy.GetAutomaticSource(system) is { } own)
            return new(own.Id, own.BrowserPage, null, own.Name, null, true,
                "PcNinja Windows OEM download matches this edition, language and architecture. " +
                "The target version is not known until the actual media is validated. " +
                "Download availability does not prove upgrade compatibility; older or mismatched media will not start Setup.");

        if (UnsupportedReason(system) is { } unsupported) return Fallback(system, unsupported);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(RequestTimeout);
        try
        {
            var page = CatalogPage(system);
            using var request = new HttpRequestMessage(HttpMethod.Get, page);
            request.Headers.Accept.ParseAdd("text/html");
            request.Headers.UserAgent.ParseAdd("PcNinja-IsoCatalog/1.0");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            // The supplied client may have redirects enabled. Never parse a redirected external host/page.
            if (response.RequestMessage?.RequestUri is { } final && !SameCatalogPage(final, page))
                return Fallback(system, "The catalog redirected outside its expected public page.");
            if (response.Content.Headers.ContentLength is > MaximumCatalogBytes)
                return Fallback(system, "The catalog exceeded the safe page-size limit.");
            if (response.Content.Headers.ContentType?.MediaType is { } type &&
                !type.Equals("text/html", StringComparison.OrdinalIgnoreCase) && !type.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase))
                return Fallback(system, "The catalog did not return an HTML page.");
            using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var bytes = new MemoryStream();
            var buffer = new byte[16384];
            while (true)
            {
                var count = await stream.ReadAsync(buffer, deadline.Token).ConfigureAwait(false);
                if (count == 0) break;
                if (bytes.Length + count > MaximumCatalogBytes) return Fallback(system, "The catalog exceeded the safe page-size limit.");
                bytes.Write(buffer, 0, count);
            }
            return Parse(Encoding.UTF8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length), system, deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return Fallback(system, "The catalog request timed out; no matching language was substituted."); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or RegexMatchTimeoutException)
        { return Fallback(system, "The catalog could not be read safely; no exact ISO was selected."); }
    }

    internal static DownloadRecommendation Parse(string html, DownloadSystem system, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        if (html.Length > MaximumCatalogBytes || UnsupportedReason(system) is { })
            return Fallback(system, "The page or system does not meet the catalog matching rules.");
        html = RemovedBlocks.Replace(html, " ");
        var matches = new List<Candidate>();
        var previousEnd = 0;
        foreach (Match table in Tables.Matches(html))
        {
            token.ThrowIfCancellationRequested();
            var prefixStart = Math.Max(previousEnd, table.Index - 16384);
            var prefix = html[prefixStart..table.Index];
            previousEnd = table.Index + table.Length;
            var headings = Headings.Matches(prefix);
            if (headings.Count == 0) continue;
            var heading = headings[^1];
            var title = Text(heading.Value);
            var header = system.IsServer ? ServerTitle.Match(title) : ClientTitle.Match(title);
            if (!header.Success) continue;
            var release = header.Groups["release"].Value.ToUpperInvariant();
            var builds = system.IsServer ? ServerBuilds : ClientBuilds;
            if (!builds.TryGetValue(release, out var targetBuild)) continue;
            var buildMatches = BuildText.Matches(Text(prefix[(heading.Index + heading.Length)..]));
            if (buildMatches.Count != 1 || !int.TryParse(buildMatches[0].Groups["build"].Value, out var advertisedBuild) ||
                !int.TryParse(buildMatches[0].Groups["revision"].Value, out var revision) || advertisedBuild != targetBuild ||
                !EligibleRelease(system, release, targetBuild)) continue;
            var expectedPack = Pack(system);
            var pack = system.IsServer ? "server" : header.Groups["pack"].Value.ToLowerInvariant();
            if (pack != expectedPack) continue;
            var rows = RowStarts.Matches(table.Value);
            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                token.ThrowIfCancellationRequested();
                var start = rows[rowIndex].Index + rows[rowIndex].Length;
                var end = rowIndex + 1 < rows.Count ? rows[rowIndex + 1].Index : table.Length;
                var row = table.Value[start..end];
                var cells = CellStarts.Matches(row);
                if (cells.Count != 3) continue;
                string Cell(int index) => row[(cells[index].Index + cells[index].Length)..
                    (index + 1 < cells.Count ? cells[index + 1].Index : row.Length)];
                var anchors = Anchors.Matches(Cell(2));
                if (anchors.Count != 1 || !Text(Cell(1)).Equals("x64", StringComparison.OrdinalIgnoreCase)) continue;
                var anchor = anchors[0];
                var file = Text(anchor.Groups["text"].Value);
                if (file.Length > 240 || file.Contains('/') || file.Contains('\\') || file.Contains('%') || file.Contains("..", StringComparison.Ordinal)) continue;
                var fileMatch = (system.IsServer ? ServerFile : ClientFile).Match(file);
                if (!fileMatch.Success || !fileMatch.Groups["language"].Value.Equals(system.Language, StringComparison.OrdinalIgnoreCase) ||
                    !fileMatch.Groups["release"].Value.Equals(release, StringComparison.OrdinalIgnoreCase) ||
                    !fileMatch.Groups["arch"].Value.Equals("x64", StringComparison.OrdinalIgnoreCase)) continue;
                if (!system.IsServer && fileMatch.Groups["pack"].Value.Replace("_editions", "", StringComparison.OrdinalIgnoreCase)
                    .Replace('_', ' ').Equals(pack, StringComparison.OrdinalIgnoreCase) == false) continue;
                if (!Languages.TryGetValue(system.Language, out var languageLabel) ||
                    !(Text(Cell(0)).Equals(languageLabel, StringComparison.OrdinalIgnoreCase) ||
                        Text(Cell(0)).Equals(system.Language, StringComparison.OrdinalIgnoreCase))) continue;
                var hrefs = Hrefs.Matches(anchor.Groups["attributes"].Value);
                if (hrefs.Count != 1) continue;
                var href = hrefs[0];
                var url = WebUtility.HtmlDecode(href.Groups["double"].Success ? href.Groups["double"].Value :
                    href.Groups["single"].Success ? href.Groups["single"].Value : href.Groups["bare"].Value);
                if (!IsPublicLanding(url)) continue;
                matches.Add(new(file, url, release, targetBuild, revision));
            }
        }
        if (matches.Count == 0) return Fallback(system, "No exact released ISO matched this edition, system UI language and architecture.");
        var newestBuild = matches.Max(candidate => candidate.Build);
        var newestRevision = matches.Where(candidate => candidate.Build == newestBuild).Max(candidate => candidate.Revision);
        var best = matches.Where(candidate => candidate.Build == newestBuild && candidate.Revision == newestRevision)
            .Distinct().ToArray();
        if (best.Length != 1) return Fallback(system, "The catalog returned ambiguous matching media; choose and verify the exact ISO manually.");
        var chosen = best[0];
        var sameFeature = chosen.Build == system.Build;
        var message = sameFeature
            ? chosen.Revision <= system.Revision ? "Same-feature repair candidate, not a newer Windows version; its media revision is equal or older."
                : "Same-feature refreshed media candidate; this is not a newer Windows feature release."
            : "Matching later released media candidate; link matching is not proof of an allowed in-place upgrade.";
        if (system.Build == 28000) message = "Windows 11 26H1 same-family repair candidate only. 26H1 is not a general upgrade route. " + message;
        if (system.IsServer) message = "Windows Server download recommendation only; this does not authorize or start a Server upgrade. " + message;
        return new("massgrave", chosen.Url, chosen.FileName, system.IsServer ? $"Windows Server {chosen.Release}" : $"Windows 11 {chosen.Release}",
            $"10.0.{chosen.Build}.{chosen.Revision}", true, message + " Open the public page and complete any human verification yourself, then select the downloaded ISO for full validation.");
    }

    internal static bool IsPublicLanding(string url)
    {
        if (url.Contains('%') || url.Contains("..", StringComparison.Ordinal) || url.Contains('\\') ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0) return false;
        return uri.IdnHost.Equals("zerofs.link", StringComparison.OrdinalIgnoreCase) ? ZeroFsPath.IsMatch(uri.AbsolutePath) :
            uri.IdnHost.Equals("buzzheavier.com", StringComparison.OrdinalIgnoreCase) && BuzzPath.IsMatch(uri.AbsolutePath);
    }

    private static string? UnsupportedReason(DownloadSystem system) =>
        !system.IsX64 ? "No exact catalog route is configured for this architecture." :
        system.IsServer ? !ServerBuilds.Values.Contains(system.Build) || Pack(system) is null
            ? "This Server release/edition has no verified catalog matching rule." : null :
        system.IsLtsc || DownloadSourcePolicy.IsLtscEdition(system.Edition) ? "LTSC/LTSB needs exact servicing-family media; no automatic substitution is allowed." :
        !system.IsSupportedClient && !(system.Build == 28000 && system.InstallationType.Equals("Client", StringComparison.OrdinalIgnoreCase))
            ? "The current client feature release is unknown or preview-only." :
        Pack(system) is null ? "The installed edition has no verified Consumer/Business media mapping." : null;

    private static string? Pack(DownloadSystem system) => system.IsServer
        ? system.Edition.Equals("ServerStandard", StringComparison.OrdinalIgnoreCase) || system.Edition.Equals("ServerDatacenter", StringComparison.OrdinalIgnoreCase) ? "server" : null
        : DownloadSourcePolicy.ConsumerEditions.Contains(system.Edition, StringComparer.OrdinalIgnoreCase) ? "consumer"
        : system.Edition.Equals("Enterprise", StringComparison.OrdinalIgnoreCase) || system.Edition.Equals("EnterpriseN", StringComparison.OrdinalIgnoreCase) ? "business"
        : system.Edition.Equals("IoTEnterprise", StringComparison.OrdinalIgnoreCase) ? "iot enterprise" : null;

    private static bool EligibleRelease(DownloadSystem system, string release, int targetBuild) =>
        system.IsServer ? targetBuild >= system.Build : system.Build == 28000 ? release == "26H1" :
        release != "26H1" && targetBuild >= system.Build;

    private static string CatalogPage(DownloadSystem system) => system.IsServer ? ServerCatalogPage : ClientCatalogPage;
    private static DownloadRecommendation Fallback(DownloadSystem system, string reason) =>
        new("massgrave", CatalogPage(system), null, null, null, false, reason +
            $" Review {CatalogPage(system)} manually, or use Microsoft's official alternative: " +
            (system.IsServer ? DownloadSourcePolicy.MicrosoftServerDownloadPage + " (check licensing and avoid an unintended evaluation conversion)." : UpgradeEngine.MicrosoftDownloadPage + ".") +
            " No English-language substitution, ISO transfer, or upgrade was performed.");
    private static bool SameCatalogPage(Uri uri, string expected) => uri.Scheme == "https" && uri.IsDefaultPort &&
        uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 &&
        uri.AbsoluteUri.TrimEnd('/').Equals(expected, StringComparison.OrdinalIgnoreCase);
    private static string Text(string html) => Whitespace.Replace(WebUtility.HtmlDecode(Tags.Replace(html, " ")), " ").Trim();
    private static Regex Pattern(string pattern) => new(pattern,
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant | Fx.NonBacktracking,
        TimeSpan.FromSeconds(1));
    private sealed record Candidate(string FileName, string Url, string Release, int Build, int Revision);
}
