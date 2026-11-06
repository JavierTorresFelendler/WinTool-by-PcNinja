using System.Text.RegularExpressions;

namespace WinTool.Office;

public sealed class OdtLink
{
    public OdtLink(string url, bool latest)
    {
        Url = url;
        Latest = latest;
    }

    public string Url { get; }

    /// <summary>True when found on Microsoft's download page; false for the fixed fallback.</summary>
    public bool Latest { get; }
}

/// <summary>
/// Where the Office Deployment Tool comes from (D-022, OF-20): the newest
/// officedeploymenttool_*.exe on Microsoft's download page, else a fixed
/// address updated with each WinTool release. Either way the file must be
/// signed by Microsoft before it runs.
/// </summary>
public static class OdtSource
{
    public const string DownloadPage = "https://www.microsoft.com/en-us/download/details.aspx?id=49117";

    /// <summary>Update with each WinTool release (OFFICE-BEHAVIOR OF-20).</summary>
    public const string FallbackUrl = "https://download.microsoft.com/download/6c1eeb25-cf8b-41d9-8d0d-cc1dbc032140/officedeploymenttool_18827-20140.exe";

    private static readonly Regex Link = new Regex(
        @"https://download\.microsoft\.com/download/[A-Za-z0-9/\-]+/officedeploymenttool_(\d+)-(\d+)\.exe",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));

    /// <summary>The newest ODT link in the page text, or null.</summary>
    public static string? FindLatest(string html)
    {
        if (string.IsNullOrEmpty(html)) return null;
        var text = html.Replace("\\/", "/");
        return Link.Matches(text).Cast<Match>()
            .Select(m => new { m.Value, Major = long.Parse(m.Groups[1].Value), Minor = long.Parse(m.Groups[2].Value) })
            .OrderByDescending(m => m.Major).ThenByDescending(m => m.Minor)
            .Select(m => m.Value)
            .FirstOrDefault();
    }

    public static async Task<OdtLink> ResolveAsync(IOfficeTools tools, Action<string> log, CancellationToken token)
    {
        try
        {
            var html = await tools.GetStringAsync(DownloadPage, token).ConfigureAwait(false);
            var latest = FindLatest(html);
            if (latest != null) return new OdtLink(latest, true);
            log("Microsoft's download page listed no Office Deployment Tool link; using the fixed address.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log("Microsoft's download page could not be read (" + ex.Message + "); using the fixed address.");
        }

        return new OdtLink(FallbackUrl, false);
    }
}
