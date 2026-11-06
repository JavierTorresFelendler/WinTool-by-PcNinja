using Xunit;

namespace WinTool.Office.Tests;

public class ConfigurationTests
{
    private static string Lines(params string[] lines) => string.Join("\r\n", lines) + "\r\n";

    /// <summary>Byte for byte what Smart Office Installer v3.3.15 writes with its defaults (Microsoft 365, Current).</summary>
    [Fact]
    public void Defaults_match_the_original_tool()
    {
        Assert.Equal(Lines(
            "<Configuration>",
            "  <Add OfficeClientEdition=\"64\" Channel=\"Current\">",
            "    <Product ID=\"O365ProPlusRetail\">",
            "      <Language ID=\"en-us\" />",
            "      <Language ID=\"he-il\" />",
            "      <ExcludeApp ID=\"Lync\" />",
            "      <ExcludeApp ID=\"Access\" />",
            "      <ExcludeApp ID=\"Publisher\" />",
            "      <ExcludeApp ID=\"Teams\" />",
            "      <ExcludeApp ID=\"OneDrive\" />",
            "    </Product>",
            "  </Add>",
            "  <Display Level=\"Full\" AcceptEULA=\"TRUE\" />",
            "  <RemoveMSI All=\"True\" />",
            "</Configuration>"), OfficeConfiguration.Build(new OfficeRequest()));
    }

    [Fact]
    public void Ltsc_with_every_option_matches_the_original_tool()
    {
        var request = new OfficeRequest
        {
            Family = OfficeFamily.Ltsc2024,
            Channel = "SemiAnnual", // ignored for LTSC, as in the original
            Apps = new List<string> { "Word", "Excel" },
            Languages = new List<string> { "he-il" },
            Visio = true,
            Project = true,
            Use32Bit = true,
            RemoveExisting = true
        };
        Assert.Equal(Lines(
            "<Configuration>",
            "  <Add SourcePath=\"D:\\Pkg &amp; Co\\OFFICE-OFFLINE\" OfficeClientEdition=\"32\" Channel=\"PerpetualVL2024\">",
            "    <Product ID=\"ProPlus2024Volume\">",
            "      <Language ID=\"he-il\" />",
            "      <ExcludeApp ID=\"Lync\" />",
            "      <ExcludeApp ID=\"PowerPoint\" />",
            "      <ExcludeApp ID=\"Outlook\" />",
            "      <ExcludeApp ID=\"OneNote\" />",
            "      <ExcludeApp ID=\"Access\" />",
            "      <ExcludeApp ID=\"Publisher\" />",
            "      <ExcludeApp ID=\"Teams\" />",
            "      <ExcludeApp ID=\"OneDrive\" />",
            "    </Product>",
            "    <Product ID=\"VisioPro2024Volume\">",
            "      <Language ID=\"he-il\" />",
            "    </Product>",
            "    <Product ID=\"ProjectPro2024Volume\">",
            "      <Language ID=\"he-il\" />",
            "    </Product>",
            "  </Add>",
            "  <Remove All=\"True\" />",
            "  <Display Level=\"Full\" AcceptEULA=\"TRUE\" />",
            "  <Property Name=\"AUTOACTIVATE\" Value=\"1\" />",
            "  <RemoveMSI All=\"True\" />",
            "</Configuration>"), OfficeConfiguration.Build(request, @"D:\Pkg & Co\OFFICE-OFFLINE"));
    }

    [Fact]
    public void Auto_activate_only_applies_to_ltsc()
    {
        Assert.DoesNotContain("AUTOACTIVATE", OfficeConfiguration.Build(new OfficeRequest { AutoActivate = true }));
        Assert.DoesNotContain("AUTOACTIVATE", OfficeConfiguration.Build(new OfficeRequest { Family = OfficeFamily.Ltsc2024, AutoActivate = false }));
    }

    [Fact]
    public void Visio_and_project_skip_languages_they_do_not_have()
    {
        var xml = OfficeConfiguration.Build(new OfficeRequest { Languages = new List<string> { "en-gb", "fr-ca" }, Visio = true });
        Assert.Contains("<Product ID=\"VisioProRetail\">\r\n      <Language ID=\"en-us\" />\r\n    </Product>", xml);
        var mixed = OfficeConfiguration.Build(new OfficeRequest { Languages = new List<string> { "es-mx", "de-de" }, Project = true });
        Assert.Contains("<Product ID=\"ProjectProRetail\">\r\n      <Language ID=\"de-de\" />\r\n    </Product>", mixed);
    }

    [Fact]
    public void Languages_keep_their_order_without_duplicates_and_ids_are_normalized()
    {
        var xml = OfficeConfiguration.Build(new OfficeRequest { Languages = new List<string> { "DE-DE", "en-us", "de-de" }, Apps = new List<string> { "word" } });
        Assert.Contains("      <Language ID=\"de-de\" />\r\n      <Language ID=\"en-us\" />\r\n      <ExcludeApp", xml);
        Assert.DoesNotContain("ExcludeApp ID=\"Word\"", xml);
    }

    [Fact]
    public void Silent_hides_the_installer()
    {
        Assert.Contains("<Display Level=\"None\" AcceptEULA=\"TRUE\" />", OfficeConfiguration.Build(new OfficeRequest { Silent = true }));
        Assert.Contains("<Display Level=\"None\"", OfficeConfiguration.RemoveAll(true));
    }

    [Fact]
    public void Remove_all_matches_the_original_tool()
    {
        Assert.Equal("<Configuration>\r\n  <Remove All=\"True\" />\r\n  <Display Level=\"Full\" AcceptEULA=\"TRUE\" />\r\n</Configuration>",
            OfficeConfiguration.RemoveAll());
    }

    [Theory]
    [InlineData("channel", "Beta")]
    [InlineData("app", "Paint")]
    [InlineData("noapps", "")]
    [InlineData("language", "xx-yy")]
    [InlineData("nolanguages", "")]
    public void Invalid_requests_are_refused(string kind, string value)
    {
        var request = new OfficeRequest();
        switch (kind)
        {
            case "channel": request.Channel = value; break;
            case "app": request.Apps.Add(value); break;
            case "noapps": request.Apps.Clear(); break;
            case "language": request.Languages.Add(value); break;
            case "nolanguages": request.Languages.Clear(); break;
        }

        Assert.NotEmpty(request.Validate());
        Assert.Throws<ArgumentException>(() => OfficeConfiguration.Build(request));
    }

    [Fact]
    public void The_catalog_has_the_44_full_language_packs()
    {
        Assert.Equal(44, OfficeCatalog.Languages.Count);
        Assert.Equal(44, OfficeCatalog.Languages.Select(l => l.Id).Distinct().Count());
        Assert.All(OfficeCatalog.Languages, l => Assert.Equal(l.Id.ToLowerInvariant(), l.Id));
        Assert.Equal(new[] { "en-gb", "fr-ca", "es-mx" }, OfficeCatalog.Languages.Where(l => !l.InVisioAndProject).Select(l => l.Id));
    }
}
