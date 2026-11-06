namespace WinTool.Office;

public enum OfficeFamily
{
    /// <summary>Office LTSC Professional Plus 2024 (volume license).</summary>
    Ltsc2024,

    /// <summary>Microsoft 365 Apps for Enterprise.</summary>
    Microsoft365
}

public sealed class OfficeApp
{
    public OfficeApp(string id, bool includedByDefault)
    {
        Id = id;
        IncludedByDefault = includedByDefault;
    }

    /// <summary>The Office Deployment Tool ExcludeApp ID, also the display name.</summary>
    public string Id { get; }
    public bool IncludedByDefault { get; }
}

public sealed class OfficeLanguage
{
    public OfficeLanguage(string id, string name, bool inVisioAndProject = true)
    {
        Id = id;
        Name = name;
        InVisioAndProject = inVisioAndProject;
    }

    /// <summary>Culture code as the configuration file writes it (lower case, like the original tool).</summary>
    public string Id { get; }

    /// <summary>English name from Microsoft's language table.</summary>
    public string Name { get; }

    /// <summary>en-GB, fr-CA and es-MX are not available in Project or Visio.</summary>
    public bool InVisioAndProject { get; }

    public override string ToString() => Name + " (" + Id + ")";
}

public sealed class OfficeChannel
{
    public OfficeChannel(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }
    public string Name { get; }
    public override string ToString() => Name;
}

/// <summary>
/// Products, apps, channels and languages (OFFICE-BEHAVIOR OF-01..OF-08).
/// Sources: Smart Office Installer v3.3.15 and Microsoft's ODT documentation.
/// </summary>
public static class OfficeCatalog
{
    public const string LtscChannel = "PerpetualVL2024";
    public const string LtscProduct = "ProPlus2024Volume";
    public const string LtscVisio = "VisioPro2024Volume";
    public const string LtscProject = "ProjectPro2024Volume";
    public const string M365Product = "O365ProPlusRetail";
    public const string M365Visio = "VisioProRetail";
    public const string M365Project = "ProjectProRetail";

    /// <summary>Apps in the order the original tool lists them; Lync (Skype for Business) is always excluded.</summary>
    public static IReadOnlyList<OfficeApp> Apps { get; } = new[]
    {
        new OfficeApp("Word", true),
        new OfficeApp("Excel", true),
        new OfficeApp("PowerPoint", true),
        new OfficeApp("Outlook", true),
        new OfficeApp("OneNote", true),
        new OfficeApp("Access", false),
        new OfficeApp("Publisher", false),
        new OfficeApp("Teams", false),
        new OfficeApp("OneDrive", false),
    };

    public static IReadOnlyList<OfficeChannel> Channels { get; } = new[]
    {
        new OfficeChannel("Current", "Current Channel"),
        new OfficeChannel("MonthlyEnterprise", "Monthly Enterprise Channel"),
        new OfficeChannel("SemiAnnual", "Semi-Annual Enterprise Channel"),
    };

    public const string DefaultChannel = "Current";

    public static IReadOnlyList<string> DefaultLanguages { get; } = new[] { "en-us", "he-il" };

    /// <summary>The full language packs of Microsoft's table (languages with companion proofing).</summary>
    public static IReadOnlyList<OfficeLanguage> Languages { get; } = new[]
    {
        new OfficeLanguage("ar-sa", "Arabic"),
        new OfficeLanguage("bg-bg", "Bulgarian"),
        new OfficeLanguage("zh-cn", "Chinese (Simplified)"),
        new OfficeLanguage("zh-tw", "Chinese (Traditional)"),
        new OfficeLanguage("hr-hr", "Croatian"),
        new OfficeLanguage("cs-cz", "Czech"),
        new OfficeLanguage("da-dk", "Danish"),
        new OfficeLanguage("nl-nl", "Dutch"),
        new OfficeLanguage("en-us", "English"),
        new OfficeLanguage("en-gb", "English UK", false),
        new OfficeLanguage("et-ee", "Estonian"),
        new OfficeLanguage("fi-fi", "Finnish"),
        new OfficeLanguage("fr-fr", "French"),
        new OfficeLanguage("fr-ca", "French Canada", false),
        new OfficeLanguage("de-de", "German"),
        new OfficeLanguage("el-gr", "Greek"),
        new OfficeLanguage("he-il", "Hebrew"),
        new OfficeLanguage("hi-in", "Hindi"),
        new OfficeLanguage("hu-hu", "Hungarian"),
        new OfficeLanguage("id-id", "Indonesian"),
        new OfficeLanguage("it-it", "Italian"),
        new OfficeLanguage("ja-jp", "Japanese"),
        new OfficeLanguage("kk-kz", "Kazakh"),
        new OfficeLanguage("ko-kr", "Korean"),
        new OfficeLanguage("lv-lv", "Latvian"),
        new OfficeLanguage("lt-lt", "Lithuanian"),
        new OfficeLanguage("ms-my", "Malay (Latin)"),
        new OfficeLanguage("nb-no", "Norwegian Bokmål"),
        new OfficeLanguage("nn-no", "Norwegian Nynorsk"),
        new OfficeLanguage("pl-pl", "Polish"),
        new OfficeLanguage("pt-pt", "Portuguese (Portugal)"),
        new OfficeLanguage("pt-br", "Portuguese (Brazil)"),
        new OfficeLanguage("ro-ro", "Romanian"),
        new OfficeLanguage("ru-ru", "Russian"),
        new OfficeLanguage("sr-latn-rs", "Serbian (Latin, Serbia)"),
        new OfficeLanguage("sk-sk", "Slovak"),
        new OfficeLanguage("sl-si", "Slovenian"),
        new OfficeLanguage("es-es", "Spanish"),
        new OfficeLanguage("es-mx", "Spanish Mexico", false),
        new OfficeLanguage("sv-se", "Swedish"),
        new OfficeLanguage("th-th", "Thai"),
        new OfficeLanguage("tr-tr", "Turkish"),
        new OfficeLanguage("uk-ua", "Ukrainian"),
        new OfficeLanguage("vi-vn", "Vietnamese"),
    };

    public static OfficeLanguage? FindLanguage(string id) =>
        Languages.FirstOrDefault(language => string.Equals(language.Id, id, StringComparison.OrdinalIgnoreCase));

    public static OfficeApp? FindApp(string id) =>
        Apps.FirstOrDefault(app => string.Equals(app.Id, id, StringComparison.OrdinalIgnoreCase));

    public static OfficeChannel? FindChannel(string id) =>
        Channels.FirstOrDefault(channel => string.Equals(channel.Id, id, StringComparison.OrdinalIgnoreCase));
}
