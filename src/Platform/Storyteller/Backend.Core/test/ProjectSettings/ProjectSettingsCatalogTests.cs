using System.Text.Json.Nodes;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

public class ProjectSettingsCatalogTests
{
    [Fact]
    public void Default_IsTheEmbeddedCatalog()
    {
        var catalog = ProjectSettingsCatalog.Default;

        catalog.ShouldBeSameAs(ProjectSettingsCatalog.Default);
        catalog.Format.ShouldBe(ProjectSettings.FileFormat);
        catalog.TypeCodes.ShouldBe([AnnotationTypeCodes.Responsibility, AnnotationTypeCodes.Unit, AnnotationTypeCodes.Subject, AnnotationTypeCodes.Usage, AnnotationTypeCodes.Context, AnnotationTypeCodes.Execution, AnnotationTypeCodes.UnitOfExecution]);
        catalog.Colors.Count.ShouldBe(18);
        catalog.Icons.Count.ShouldBe(65);
        catalog.DateFormats.ShouldBe(["yyyy-mm-dd", "dd.mm.yyyy", "dd/mm/yyyy", "mm/dd/yyyy", "d mmm yyyy"]);
        catalog.DefaultTimeZone.ShouldBe("UTC");
        catalog.DefaultDateFormat.ShouldBe("yyyy-mm-dd");
        catalog.LabelMaxLength.ShouldBe(40);
        catalog.PluralMaxLength.ShouldBe(40);
    }

    [Fact]
    public void TheTypeCodes_AreTheCodesOfTheAnnotationTypes()
    {
        ProjectSettingsCatalog.Default.TypeCodes.ShouldBe(AnnotationTypeCodes.ValidCodes.Keys, ignoreOrder: true);
    }

    [Fact]
    public void EveryTypeCode_HasACompleteBuiltInAppearance()
    {
        var catalog = ProjectSettingsCatalog.Default;

        catalog.BuiltIn.Keys.ShouldBe(catalog.TypeCodes, ignoreOrder: true);
        catalog.BuiltIn[AnnotationTypeCodes.Subject].ShouldBe(new ProjectTypeAppearance { Label = "Subject", Plural = "Subjects", Color = "blue", Icon = "i-lucide-sun" });
        catalog.BuiltIn[AnnotationTypeCodes.UnitOfExecution].ShouldBe(new ProjectTypeAppearance { Label = "Unit of execution", Plural = "Units of execution", Color = "orange", Icon = "i-lucide-cpu" });

        foreach (var appearance in catalog.BuiltIn.Values)
        {
            catalog.Colors.ShouldContain(appearance.Color!);
            catalog.Icons.ShouldContain(appearance.Icon!);
        }
    }

    // The tz database's ids, canonical zones and links, sorted by ordinal comparison, none twice in any case.
    [Fact]
    public void TheTimeZones_AreTheIdsOfTheTzDatabase()
    {
        var zones = ProjectSettingsCatalog.Default.TimeZones;

        zones.ShouldBe(zones.Order(StringComparer.Ordinal));
        zones.Select(zone => zone.ToLowerInvariant()).ShouldBeUnique();
        zones.ShouldContain("UTC");
        zones.ShouldContain("Etc/UTC");
        zones.ShouldContain("Europe/Prague");
        zones.ShouldContain("America/Phoenix");
        zones.ShouldContain("Asia/Kolkata");
        zones.ShouldContain("Asia/Calcutta");
        zones.ShouldNotContain("Central Europe Standard Time");
        zones.Count.ShouldBeGreaterThan(500);
    }

    [Theory]
    [InlineData("SBT", "sbt")]
    [InlineData("sbt", "sbt")]
    [InlineData("Uxe", "uxe")]
    public void TryGetTypeCode_MatchesInAnyCase(string value, string expected)
    {
        ProjectSettingsCatalog.Default.TryGetTypeCode(value, out var code).ShouldBeTrue();
        code.ShouldBe(expected);
    }

    [Theory]
    [InlineData("Subject")]
    [InlineData("s")]
    [InlineData("")]
    [InlineData(" sbt")]
    [InlineData(null)]
    public void TryGetTypeCode_RefusesAnythingElse(string? value)
    {
        ProjectSettingsCatalog.Default.TryGetTypeCode(value, out var code).ShouldBeFalse();
        code.ShouldBeNull();
    }

    [Fact]
    public void TheValues_ComeBackInTheCatalogsSpelling()
    {
        var catalog = ProjectSettingsCatalog.Default;

        catalog.TryGetColor("Fuchsia", out var color).ShouldBeTrue();
        color.ShouldBe("fuchsia");
        catalog.TryGetIcon("I-LUCIDE-GIT-BRANCH", out var icon).ShouldBeTrue();
        icon.ShouldBe("i-lucide-git-branch");
        catalog.TryGetDateFormat("D MMM YYYY", out var dateFormat).ShouldBeTrue();
        dateFormat.ShouldBe("d mmm yyyy");
        catalog.TryGetTimeZone("AMERICA/NEW_YORK", out var timeZone).ShouldBeTrue();
        timeZone.ShouldBe("America/New_York");
        catalog.TryGetTimeZone("utc", out timeZone).ShouldBeTrue();
        timeZone.ShouldBe("UTC");
    }

    // Case folds in ASCII only, as in every runtime: a long s (U+017F) or a Kelvin sign (U+212A) is no s or k.
    [Theory]
    [InlineData("ſbt")]
    [InlineData("Europe/KiKev")]
    [InlineData("Key")]
    public void NonAsciiLetters_AreNotFolded(string value)
    {
        var catalog = ProjectSettingsCatalog.Default;

        catalog.TryGetTypeCode(value, out _).ShouldBeFalse();
        catalog.TryGetTimeZone(value, out _).ShouldBeFalse();
        catalog.TryGetIcon("i-lucide-Key", out _).ShouldBeFalse();
    }

    [Fact]
    public void IndexOfTypeCode_IsThePositionInTheCatalog()
    {
        var catalog = ProjectSettingsCatalog.Default;

        catalog.IndexOfTypeCode("rst").ShouldBe(0);
        catalog.IndexOfTypeCode("SBT").ShouldBe(2);
        catalog.IndexOfTypeCode("uxe").ShouldBe(6);
        catalog.IndexOfTypeCode("abc").ShouldBe(-1);
    }

    [Fact]
    public void Parse_ReadsTheCatalogOfASuiteFile()
    {
        var catalog = ProjectSettingsCatalog.Parse(ProjectSettingsFiles.ReadText(ProjectSettingsFiles.Conformance));

        catalog.TimeZones.ShouldBe(ProjectSettingsCatalog.Default.TimeZones);
        catalog.Icons.ShouldBe(ProjectSettingsCatalog.Default.Icons);
    }

    [Theory]
    [InlineData("""{ }""")]
    [InlineData("""{ "catalog": [] }""")]
    public void Parse_WithoutACatalog_Throws(string json)
    {
        Should.Throw<FormatException>(() => ProjectSettingsCatalog.Parse(json));
    }

    [Theory]
    [InlineData("format", "\"2s.project-settings.v2\"")]
    [InlineData("colors", "[]")]
    [InlineData("colors", "[ \"red\", \"RED\" ]")]
    [InlineData("icons", "[ 1 ]")]
    [InlineData("limits", "{ \"label\": 0, \"plural\": 40 }")]
    [InlineData("defaults", "{ \"timeZone\": \"Mars/Base\", \"dateFormat\": \"yyyy-mm-dd\" }")]
    [InlineData("defaults", "{ \"timeZone\": \"utc\", \"dateFormat\": \"yyyy-mm-dd\" }")]
    [InlineData("builtIn", "{}")]
    public void Parse_AnInvalidCatalog_Throws(string member, string value)
    {
        var file = ProjectSettingsFiles.Read(ProjectSettingsFiles.Conformance);
        file["catalog"]![member] = JsonNode.Parse(value);

        Should.Throw<FormatException>(() => ProjectSettingsCatalog.Parse(file.ToJsonString()));
    }
}
