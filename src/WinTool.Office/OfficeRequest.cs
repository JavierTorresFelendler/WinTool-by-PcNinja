using System.Security;
using System.Text;

namespace WinTool.Office;

/// <summary>What to install or package. Defaults are the original tool's defaults.</summary>
public sealed class OfficeRequest
{
    public OfficeFamily Family { get; set; } = OfficeFamily.Microsoft365;

    /// <summary>Microsoft 365 update channel; LTSC always uses PerpetualVL2024.</summary>
    public string Channel { get; set; } = OfficeCatalog.DefaultChannel;

    /// <summary>App IDs to install; every other catalog app is excluded.</summary>
    public IList<string> Apps { get; set; } = OfficeCatalog.Apps.Where(app => app.IncludedByDefault).Select(app => app.Id).ToList();

    /// <summary>Language IDs; the first is the primary (Windows shell) language.</summary>
    public IList<string> Languages { get; set; } = OfficeCatalog.DefaultLanguages.ToList();

    public bool Visio { get; set; }
    public bool Project { get; set; }
    public bool Use32Bit { get; set; }

    /// <summary>LTSC only: activate the volume license during installation (on by default).</summary>
    public bool AutoActivate { get; set; } = true;

    /// <summary>Remove every installed Click-to-Run Office product first.</summary>
    public bool RemoveExisting { get; set; }

    /// <summary>Hide the Office installer window (command line --quiet).</summary>
    public bool Silent { get; set; }

    /// <summary>Problems that stop the request, in English. Empty when valid.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Family == OfficeFamily.Microsoft365 && OfficeCatalog.FindChannel(Channel ?? string.Empty) == null)
            errors.Add("Unknown update channel: " + Channel + ". Use " + string.Join(", ", OfficeCatalog.Channels.Select(c => c.Id)) + ".");
        foreach (var app in Apps ?? new List<string>())
            if (OfficeCatalog.FindApp(app) == null)
                errors.Add("Unknown app: " + app + ". Use " + string.Join(", ", OfficeCatalog.Apps.Select(a => a.Id)) + ".");
        if (Apps == null || Apps.Count == 0) errors.Add("Choose at least one app.");
        foreach (var language in Languages ?? new List<string>())
            if (OfficeCatalog.FindLanguage(language) == null)
                errors.Add("Unknown language: " + language + ".");
        if (Languages == null || Languages.Count == 0) errors.Add("Choose at least one language.");
        return errors;
    }
}

/// <summary>Office Deployment Tool configuration files (OF-10..OF-16).</summary>
public static class OfficeConfiguration
{
    private const string NewLine = "\r\n";

    /// <summary>The /configure or /download file. sourcePath is set only for /download.</summary>
    public static string Build(OfficeRequest request, string? sourcePath = null)
    {
        var errors = request.Validate();
        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));

        var ltsc = request.Family == OfficeFamily.Ltsc2024;
        var languages = Distinct(request.Languages.Select(id => OfficeCatalog.FindLanguage(id)!.Id));
        var apps = new HashSet<string>(request.Apps.Select(id => OfficeCatalog.FindApp(id)!.Id), StringComparer.OrdinalIgnoreCase);
        var source = string.IsNullOrWhiteSpace(sourcePath) ? string.Empty : " SourcePath=\"" + SecurityElement.Escape(sourcePath) + "\"";
        var channel = ltsc ? OfficeCatalog.LtscChannel : OfficeCatalog.FindChannel(request.Channel)!.Id;

        var b = new StringBuilder();
        Line(b, "<Configuration>");
        Line(b, "  <Add" + source + " OfficeClientEdition=\"" + (request.Use32Bit ? "32" : "64") + "\" Channel=\"" + channel + "\">");
        Line(b, "    <Product ID=\"" + (ltsc ? OfficeCatalog.LtscProduct : OfficeCatalog.M365Product) + "\">");
        foreach (var language in languages) Line(b, "      <Language ID=\"" + language + "\" />");
        Line(b, "      <ExcludeApp ID=\"Lync\" />");
        foreach (var app in OfficeCatalog.Apps)
            if (!apps.Contains(app.Id)) Line(b, "      <ExcludeApp ID=\"" + app.Id + "\" />");
        Line(b, "    </Product>");
        if (request.Visio) AddProduct(b, ltsc ? OfficeCatalog.LtscVisio : OfficeCatalog.M365Visio, languages);
        if (request.Project) AddProduct(b, ltsc ? OfficeCatalog.LtscProject : OfficeCatalog.M365Project, languages);
        Line(b, "  </Add>");
        if (request.RemoveExisting) Line(b, "  <Remove All=\"True\" />");
        Line(b, "  <Display Level=\"" + (request.Silent ? "None" : "Full") + "\" AcceptEULA=\"TRUE\" />");
        if (ltsc && request.AutoActivate) Line(b, "  <Property Name=\"AUTOACTIVATE\" Value=\"1\" />");
        Line(b, "  <RemoveMSI All=\"True\" />");
        Line(b, "</Configuration>");
        return b.ToString();
    }

    /// <summary>Removes every Click-to-Run Office product (OF-14).</summary>
    public static string RemoveAll(bool silent = false) =>
        "<Configuration>" + NewLine +
        "  <Remove All=\"True\" />" + NewLine +
        "  <Display Level=\"" + (silent ? "None" : "Full") + "\" AcceptEULA=\"TRUE\" />" + NewLine +
        "</Configuration>";

    private static void AddProduct(StringBuilder b, string productId, IList<string> languages)
    {
        // en-GB, fr-CA and es-MX do not exist for Visio and Project (OF-13).
        var usable = languages.Where(id => OfficeCatalog.FindLanguage(id)!.InVisioAndProject).ToList();
        if (usable.Count == 0) usable.Add("en-us");
        Line(b, "    <Product ID=\"" + productId + "\">");
        foreach (var language in usable) Line(b, "      <Language ID=\"" + language + "\" />");
        Line(b, "    </Product>");
    }

    private static IList<string> Distinct(IEnumerable<string> ids)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return ids.Where(seen.Add).ToList();
    }

    private static void Line(StringBuilder b, string text) => b.Append(text).Append(NewLine);
}
