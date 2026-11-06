// Ported from Win-Upgrade-Tool-by-PcNinja tests/PcNinja.Win11Upgrade.SmokeTests (see docs/UPSTREAM-SYNC.md).
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using WinTool.Upgrade;

namespace WinTool.Upgrade.Tests.Checks;

internal static class DownloadSourceChecks
{
    public static async Task RunAsync()
    {
        CheckPolicy();
        await CheckPreDownloadRejectionAsync();
        CheckCatalog();
        await CheckNetworkAsync();
        Console.WriteLine("PASS: Matched own-source priority, 38 exact cultures, Consumer/Business/N, Server recommendations, release-family/26H1 gates, public landing allowlist and bounded/cancellable HTML only. No ISO downloaded or Setup launched.");
    }

    private static DownloadSystem SystemFor(string language = "es-ES", string edition = "Professional", int build = 26200,
        int revision = 9999, string installation = "Client", string architecture = "x64", bool ltsc = false) =>
        new(edition, language, build, revision, architecture, installation, ltsc);

    private static void CheckPolicy()
    {
        Require(DownloadSourcePolicy.Sources.Count >= 1 && DownloadSourcePolicy.Sources[0].Id == "pcninja-oem",
            "Own source priority was lost.");
        var editions = DownloadSourcePolicy.Sources[0].Editions;
        Require(DownloadSourcePolicy.OemPage == "https://winoemiso.pcninja.pro/" &&
            UpgradeEngine.DownloadEndpoint == DownloadSourcePolicy.OemPage,
            "Automatic transport and own-source browser fallback must use the final OEM link.");
        Require(editions.Count == 11 && editions.Distinct(StringComparer.OrdinalIgnoreCase).Count() == 11,
            "The configured OEM edition set changed.");
        foreach (var edition in editions)
        foreach (var build in new[] { 19045, 22000, 22631, 26100, 26200, 26300, 26220, 28000, 99999 })
        {
            var system = SystemFor("en-US", edition, build);
            Require(DownloadSourcePolicy.GetAutomaticSource(system)?.Id == "pcninja-oem" &&
                DownloadSourcePolicy.GetPcNinjaBrowserPage(system) == DownloadSourcePolicy.OemPage &&
                DownloadSourcePolicy.GetUnavailableReason(system) is null, "An approved OEM edition lost its own source.");
        }
        foreach (var system in new[]
        {
            SystemFor(), SystemFor("en-US", "Enterprise"), SystemFor("en-US", "EnterpriseN"), SystemFor("en-US", "UnknownEdition"),
            SystemFor("en-US", build: 9600), SystemFor("en-US", build: 20000),
            SystemFor("en-US", architecture: "arm64"), SystemFor("en-US", architecture: "x86"),
            SystemFor("en-US", "EnterpriseS"), SystemFor("en-US", ltsc: true),
            SystemFor("en-US", "ServerStandard", 26100, installation: "Server"),
            SystemFor("en-US", installation: "Unknown")
        })
            Require(DownloadSourcePolicy.GetAutomaticSource(system) is null &&
                DownloadSourcePolicy.GetPcNinjaBrowserPage(system) == DownloadSourcePolicy.PcNinjaCatalogPage &&
                DownloadSourcePolicy.GetUnavailableReason(system) is not null, "Unsupported system inherited the automatic OEM source.");
        var reason = DownloadSourcePolicy.GetUnavailableReason(SystemFor())!;
        Require(reason.Contains("es-ES") && reason.Contains("en-US"), "Configured/source language mismatch was not explained.");
        Require(SystemFor(installation: "Server Core").IsServer && !SystemFor(installation: "Server Core").IsSupportedClient,
            "Server Core became an ordinary client route.");
        Require(DownloadSourcePolicy.GetMicrosoftBrowserPage(SystemFor()) == UpgradeEngine.MicrosoftDownloadPage &&
            DownloadSourcePolicy.GetMicrosoftBrowserPage(SystemFor(installation: "Server")) == DownloadSourcePolicy.MicrosoftServerDownloadPage,
            "The Microsoft alternative crossed client/Server product families.");
        Require(!SystemFor(build: 28000).IsSupportedClient && !SystemFor(build: 26220).IsSupportedClient,
            "External catalog feature-family restrictions changed with own-source download coverage.");
        var olderImage = new InstallationImage(7, "Pro N fixture", "ProfessionalN", "x64", "10.0.26100.1", "Client", "en-US");
        var olderMedia = new MountedSource(@"X:\fixture", @"X:\fixture\setup.exe", @"X:\fixture\sources\install.wim", [olderImage]);
        var newerHost = new CurrentWindows("ProfessionalN", "en-US", 28000);
        Require(DownloadSourcePolicy.GetAutomaticSource(SystemFor("en-US", "ProfessionalN", 28000)) is not null &&
            InstallationImages.Compatible(olderMedia, newerHost).Count == 0 &&
            InstallationImages.GetIncompatibility(olderImage, newerHost)!.Contains("equal-or-newer"),
            "Download eligibility bypassed actual Windows 11 downgrade protection.");
        foreach (var mismatch in new[] { olderImage with { Edition = "Professional" },
            olderImage with { Language = "es-ES" }, olderImage with { Architecture = "arm64" } })
            Require(InstallationImages.GetIncompatibility(mismatch, newerHost) is not null,
                "OEM coverage bypassed actual edition/language/architecture validation.");
    }

    private static async Task CheckPreDownloadRejectionAsync()
    {
        foreach (var profile in new[]
        {
            SystemFor(), SystemFor("en-GB"), SystemFor("he-IL"), SystemFor("en-US", "Enterprise"), SystemFor("en-US", "EnterpriseN"),
            SystemFor("en-US", "EnterpriseS", ltsc: true), SystemFor("en-US", "EnterpriseSN", ltsc: true),
            SystemFor("en-US", "ServerStandard", 26100, installation: "Server"),
            SystemFor("en-US", architecture: "arm64"), SystemFor("en-US", architecture: "x86"),
            SystemFor("en-US", build: 9600), SystemFor("en-US", build: 20000),
            SystemFor("en-US", "Unknown"), SystemFor("en-US", installation: "Unknown")
        })
        {
            using var handler = new FixtureHandler((_, _) => throw new Exception("Rejected profile reached HTTP."));
            using var http = new HttpClient(handler);
            var reads = 0;
            var engine = new UpgradeEngine(() => { reads++; return profile; }, http);
            var destination = Path.Combine(AppContext.BaseDirectory, "preflight-fixture-" + Guid.NewGuid().ToString("N"));
            try
            {
                try
                {
                    await engine.DownloadIsoAsync(destination, null, CancellationToken.None);
                    throw new Exception("An unsupported automatic profile started a transfer.");
                }
                catch (InvalidOperationException ex) when (ex.Message == DownloadSourcePolicy.GetUnavailableReason(profile)) { }
                Require(reads == 1 && handler.Calls == 0 && !Directory.Exists(destination),
                    "Automatic rejection happened after HTTP or destination creation.");
            }
            finally { if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true); }
        }
        Console.WriteLine("PASS: Actual Core automatic entry rejects incompatible profiles before HTTP/destination creation; fake transport only.");
    }

    private static void CheckCatalog()
    {
        var all = Section("consumer", "26H2", 26300, 9457, IsoDownloadCatalog.Languages.Keys);
        Require(IsoDownloadCatalog.Languages.Count == 38, "The catalog language fixture does not cover 38 cultures.");
        foreach (var language in IsoDownloadCatalog.Languages.Keys)
        {
            var recommendation = IsoDownloadCatalog.Parse(all, SystemFor(language));
            Require(recommendation.ExactMatch && recommendation.FileName!.StartsWith(language + "_", StringComparison.OrdinalIgnoreCase),
                "An exact culture was not selected, or another language was substituted: " + language);
        }
        foreach (var edition in DownloadSourcePolicy.Sources[0].Editions)
            Require(IsoDownloadCatalog.Parse(all, SystemFor(edition: edition)).ExactMatch,
                "Consumer pack did not retain an approved edition/N variant.");
        Require(IsoDownloadCatalog.Parse(all, SystemFor("es-MX")).FileName!.StartsWith("es-mx_", StringComparison.OrdinalIgnoreCase),
            "Spanish Mexico was confused with Spanish Spain.");
        var business = Section("business", "26H2", 26300, 9457, ["es-ES"]);
        foreach (var edition in new[] { "Enterprise", "EnterpriseN" })
        {
            var recommendation = IsoDownloadCatalog.Parse(all + business, SystemFor(edition: edition));
            Require(recommendation.ExactMatch && recommendation.FileName!.Contains("_business_"), "Enterprise/N did not use Business media.");
            Require(!IsoDownloadCatalog.Parse(all, SystemFor(edition: edition)).ExactMatch, "Enterprise was assigned Consumer media.");
        }
        Require(!IsoDownloadCatalog.Parse(business, SystemFor()).ExactMatch, "An unrequested Business pack was substituted.");
        foreach (var system in new[] { SystemFor(edition: "EnterpriseS"), SystemFor(edition: "EnterpriseSN"),
            SystemFor(ltsc: true), SystemFor(edition: "Unknown"), SystemFor("xx-YY"), SystemFor(architecture: "arm64") })
        {
            var recommendation = IsoDownloadCatalog.Parse(all + business, system);
            Require(!recommendation.ExactMatch && recommendation.PageUrl == IsoDownloadCatalog.ClientCatalogPage &&
                recommendation.Message.Contains("microsoft.com"), "An unmatched system lost its manual alternatives.");
        }

        var trap = Section("consumer", "26H1", 28000, 3231, ["es-ES"]);
        Require(IsoDownloadCatalog.Parse(trap + all, SystemFor()).TargetVersion == "10.0.26300.9457",
            "Numerically higher device-only 26H1 trapped an ordinary upgrade recommendation.");
        Require(!IsoDownloadCatalog.Parse(trap, SystemFor()).ExactMatch, "26H1 was offered as a general upgrade.");
        Require(!IsoDownloadCatalog.Parse(all, SystemFor(build: 28000)).ExactMatch, "26H1 was offered older other-family media.");
        var repair = IsoDownloadCatalog.Parse(all + trap, SystemFor(build: 28000));
        Require(repair.ExactMatch && repair.TargetVersion == "10.0.28000.3231" && repair.Message.Contains("repair"),
            "Current 26H1 did not retain a separate same-family repair recommendation.");
        Require(!IsoDownloadCatalog.Parse(Section("consumer", "24H2", 26100, 9000, ["es-ES"]), SystemFor()).ExactMatch,
            "Older feature media was selected.");
        var equal = IsoDownloadCatalog.Parse(all, SystemFor(build: 26300));
        Require(equal.ExactMatch && equal.Message.Contains("repair") && equal.Message.Contains("older"),
            "Equal feature/older revision was incorrectly called a newer version.");
        Require(!IsoDownloadCatalog.Parse(Section("consumer", "27H2", 29000, 1, ["es-ES"]), SystemFor()).ExactMatch,
            "An unknown future feature release was trusted.");
        Require(!IsoDownloadCatalog.Parse(all.Replace("Build - 26300.9457", "Build - 28000.9457"), SystemFor()).ExactMatch,
            "Conflicting heading/build metadata was trusted.");

        var server = ServerSection("2025", 26100, 33650, "es-ES");
        foreach (var edition in new[] { "ServerStandard", "ServerDatacenter" })
        foreach (var installation in new[] { "Server", "Server Core" })
        {
            var recommendation = IsoDownloadCatalog.Parse(server, SystemFor(edition: edition, build: 20348, installation: installation));
            Require(recommendation.ExactMatch && recommendation.TargetRelease == "Windows Server 2025" &&
                recommendation.Message.Contains("recommendation only"), "Server download selection implied an upgrade command.");
        }
        Require(!IsoDownloadCatalog.Parse(server, SystemFor(edition: "ServerStandardEval", build: 20348, installation: "Server")).ExactMatch,
            "Evaluation Server media/edition was silently mapped to licensed media.");
        Require(!IsoDownloadCatalog.Parse(ServerSection("2022", 20348, 5761, "es-ES"),
            SystemFor(edition: "ServerDatacenter", build: 26100, installation: "Server")).ExactMatch, "Older Server release was selected.");

        var baseRow = Section("consumer", "26H2", 26300, 9457, ["es-ES"]);
        foreach (var bad in new[]
        {
            baseRow.Replace(".iso</a>", ".exe</a>"), baseRow.Replace(".iso</a>", ".iso then click</a>"),
            baseRow.Replace("_consumer_editions_", "_business_editions_"),
            baseRow.Replace("es-es_windows_", "es-mx_windows_"), baseRow.Replace(">Spanish<td", ">Spanish-Mexico<td"),
            baseRow.Replace("<td>x64<td>", "<td>arm64<td>"),
            baseRow.Replace("<strong>", "<em>").Replace("</strong>", "</em>"),
            baseRow.Replace("es-es_windows_", "../es-es_windows_"),
            "<script>" + baseRow + "</script>",
            baseRow.Replace("</a>", "</a><a href='https://zerofs.link/f/other/'>another.iso</a>")
        }) Require(!IsoDownloadCatalog.Parse(bad, SystemFor()).ExactMatch, "Changed/ambiguous/non-ISO catalog markup supplied a match.");
        Require(IsoDownloadCatalog.Parse(baseRow.Replace("<strong>", "<h3>").Replace("</strong>", "</h3>"), SystemFor()).ExactMatch,
            "An ordinary heading could not replace the current strong section marker.");
        var ambiguous = baseRow + baseRow.Replace("/safeToken/", "/anotherToken/");
        Require(!IsoDownloadCatalog.Parse(ambiguous, SystemFor()).ExactMatch, "Two different landing pages were silently treated as one exact result.");
        foreach (var badUrl in new[] { "http://zerofs.link/f/x/", "https://zerofs.link.evil/f/x/", "https://evil.test/f/x/",
            "https://user@zerofs.link/f/x/", "https://zerofs.link:444/f/x/", "https://zerofs.link/f/../api/",
            "https://zerofs.link/f/%2e%2e/api/", "https://zerofs.link/f/x/?download=secret", "https://zerofs.link/f/x/#secret",
            "https://zerofs.link/api/download", "https://buzzheavier.com/api/download", "file:///C:/file.iso" })
        {
            Require(!IsoDownloadCatalog.IsPublicLanding(badUrl), "An unsafe/nonpublic download URL was trusted.");
            Require(!IsoDownloadCatalog.Parse(baseRow.Replace("https://zerofs.link/f/safeToken/", badUrl), SystemFor()).ExactMatch,
                "An unsafe URL escaped catalog filtering.");
        }
        Require(IsoDownloadCatalog.IsPublicLanding("https://zerofs.link/f/safeToken/") &&
            IsoDownloadCatalog.IsPublicLanding("https://buzzheavier.com/abc123def456"), "Supported public landing hosts were rejected.");
    }

    private static async Task CheckNetworkAsync()
    {
        using var ownHandler = new FixtureHandler((_, _) => throw new Exception("An own-source match contacted the external catalog."));
        using var ownHttp = new HttpClient(ownHandler);
        foreach (var profile in new[] { SystemFor("en-US", build: 26300), SystemFor("en-US", "ProfessionalN", 28000) })
        {
            var own = await IsoDownloadCatalog.ResolveAsync(ownHttp, profile, CancellationToken.None);
            Require(own.ExactMatch && own.Source == "pcninja-oem" && ownHandler.Calls == 0 &&
                own.PageUrl == DownloadSourcePolicy.OemPage && own.TargetVersion is null &&
                own.TargetRelease == "PcNinja Windows OEM" && own.Message.Contains("not known"),
                "Own source priority invented an unvalidated target version or lost profile coverage.");
        }

        var html = Section("consumer", "26H2", 26300, 9457, ["es-ES"]);
        using var handler = new FixtureHandler((request, _) =>
        {
            Require(request.Method == HttpMethod.Get && request.RequestUri!.AbsoluteUri == IsoDownloadCatalog.ClientCatalogPage &&
                request.Headers.UserAgent.ToString() == "PcNinja-IsoCatalog/1.0", "Unexpected catalog request or unstable user agent.");
            return Task.FromResult(Response(request, new StringContent(html, Encoding.UTF8, "text/html")));
        });
        using var http = new HttpClient(handler);
        Require((await IsoDownloadCatalog.ResolveAsync(http, SystemFor(), CancellationToken.None)).ExactMatch && handler.Calls == 1,
            "Bounded catalog GET did not resolve exact media.");
        foreach (var content in new HttpContent[]
        {
            OversizedHeader(html), new ChunkedContent(new byte[IsoDownloadCatalog.MaximumCatalogBytes + 1]),
            new StringContent(html, Encoding.UTF8, "application/octet-stream")
        })
        {
            using var oversizedHandler = new FixtureHandler((request, _) => Task.FromResult(Response(request, content)));
            using var oversizedHttp = new HttpClient(oversizedHandler);
            Require(!(await IsoDownloadCatalog.ResolveAsync(oversizedHttp, SystemFor(), CancellationToken.None)).ExactMatch,
                "A size/content-type bound was ignored.");
        }
        using var noRedirectHandler = new FixtureHandler((request, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            RequestMessage = request,
            Headers = { Location = new Uri("https://redirect.invalid/never-follow") }
        }));
        using var noRedirectHttp = new HttpClient(noRedirectHandler);
        Require(!(await IsoDownloadCatalog.ResolveAsync(noRedirectHttp, SystemFor(), CancellationToken.None)).ExactMatch &&
            noRedirectHandler.Calls == 1, "A redirect supplied a match or caused another catalog request.");
        using var errorHandler = new FixtureHandler((_, _) => throw new HttpRequestException("offline"));
        using var errorHttp = new HttpClient(errorHandler);
        var failure = await IsoDownloadCatalog.ResolveAsync(errorHttp, SystemFor(), CancellationToken.None);
        Require(!failure.ExactMatch && failure.Message.Contains("microsoft.com") && failure.PageUrl == IsoDownloadCatalog.ClientCatalogPage,
            "Network failure lost exact/manual guidance.");
        using var timeoutHandler = new FixtureHandler((_, _) => throw new OperationCanceledException());
        using var timeoutHttp = new HttpClient(timeoutHandler);
        Require(!(await IsoDownloadCatalog.ResolveAsync(timeoutHttp, SystemFor(), CancellationToken.None)).ExactMatch,
            "A catalog timeout supplied exact media.");
        using var cancellation = new CancellationTokenSource();
        using var cancelHandler = new FixtureHandler(async (_, token) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.Infinite, token);
            throw new Exception("Cancelled request resumed.");
        });
        using var cancelHttp = new HttpClient(cancelHandler);
        try
        {
            await IsoDownloadCatalog.ResolveAsync(cancelHttp, SystemFor(), cancellation.Token);
            throw new Exception("Catalog user cancellation became a fallback success.");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        using var redirectHandler = new FixtureHandler((_, _) => Task.FromResult(Response(
            new HttpRequestMessage(HttpMethod.Get, "https://evil.test/catalog"), new StringContent(html))));
        using var redirectHttp = new HttpClient(redirectHandler);
        Require(!(await IsoDownloadCatalog.ResolveAsync(redirectHttp, SystemFor(), CancellationToken.None)).ExactMatch,
            "A redirected untrusted catalog supplied media.");
    }

    private static string Section(string pack, string release, int build, int revision, IEnumerable<string> languages) =>
        $"<p><strong>Windows 11 {char.ToUpperInvariant(pack[0]) + pack[1..]} {release}</strong><br/>Build - {build}.{revision}<table><thead><tr><th>Language<th>Arch<th>Link<tbody>" +
        string.Concat(languages.Select(language => Row(language, $"{language.ToLowerInvariant()}_windows_11_{pack}_editions_version_{release.ToLowerInvariant()}_x64_dvd_1234abcd.iso"))) + "</table>";

    private static string ServerSection(string release, int build, int revision, string language) =>
        $"<p><strong>Windows Server {release}</strong><p>Build - {build}.{revision}<table><tr><th>Language<th>Arch<th>Link" +
        Row(language, $"{language.ToLowerInvariant()}_windows_server_{release}_updated_oct_2026_x64_dvd_1234abcd.iso") + "</table>";

    private static string Row(string language, string file) =>
        $"<tr><td>{IsoDownloadCatalog.Languages[language]}<td>x64<td><a href=https://zerofs.link/f/safeToken/ target=_blank>{file}</a>";

    private static HttpContent OversizedHeader(string html)
    {
        var content = new StringContent(html, Encoding.UTF8, "text/html");
        content.Headers.ContentLength = IsoDownloadCatalog.MaximumCatalogBytes + 1L;
        return content;
    }

    private static HttpResponseMessage Response(HttpRequestMessage request, HttpContent content) =>
        new(HttpStatusCode.OK) { RequestMessage = request, Content = content };

    private sealed class FixtureHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return send(request, cancellationToken);
        }
    }

    private sealed class ChunkedContent(byte[] bytes) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes, 0, bytes.Length);
        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
