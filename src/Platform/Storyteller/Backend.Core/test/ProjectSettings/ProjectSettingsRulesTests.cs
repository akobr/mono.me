namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

public class ProjectSettingsRulesTests
{
    private static readonly AuditStamp Stamp = AuditStamp.Of(AuditActor.Account("user-1", "Ada"), new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public void Normalize_TheDefaults_StaysTheDefaults()
    {
        var check = ProjectSettingsRules.Normalize(new ProjectSettings());

        check.IsValid.ShouldBeTrue();
        check.Problems.ShouldBeEmpty();
        check.Settings.Types.ShouldBeEmpty();
        check.Settings.DefaultTimeZone.ShouldBeNull();
        check.Settings.DateFormat.ShouldBeNull();
    }

    [Fact]
    public void Normalize_StoresEveryValueInItsCanonicalForm()
    {
        var input = new ProjectSettings
        {
            Types = new Dictionary<string, ProjectTypeAppearance>
            {
                ["UXE"] = new() { Label = " Job instance ", Color = "Gray", Icon = "I-Lucide-Zap" },
                ["sbt"] = new() { Label = "Company", Plural = "\tCompanies ", Color = "PINK", Icon = "i-lucide-building-2" },
                ["Rst"] = new() { Label = "   ", Plural = string.Empty },
            },
            DefaultTimeZone = "america/phoenix",
            DateFormat = "DD.MM.YYYY",
        };

        var check = ProjectSettingsRules.Normalize(input);

        check.Problems.ShouldBeEmpty();
        check.Settings!.Types.Keys.ShouldBe(["sbt", "uxe"]);
        check.Settings.Types["sbt"].ShouldBe(new ProjectTypeAppearance { Label = "Company", Plural = "Companies", Color = "pink", Icon = "i-lucide-building-2" });
        check.Settings.Types["uxe"].ShouldBe(new ProjectTypeAppearance { Label = "Job instance", Color = "gray", Icon = "i-lucide-zap" });
        check.Settings.DefaultTimeZone.ShouldBe("America/Phoenix");
        check.Settings.DateFormat.ShouldBe("dd.mm.yyyy");
    }

    [Fact]
    public void Normalize_KeepsTheVersionAndTheStamp()
    {
        var check = ProjectSettingsRules.Normalize(new ProjectSettings { Version = 7, Modified = Stamp, DateFormat = "yyyy-mm-dd" });

        check.Settings!.Version.ShouldBe(7);
        check.Settings.Modified.ShouldBe(Stamp);
        check.Settings.DateFormat.ShouldBe("yyyy-mm-dd");
    }

    [Fact]
    public void Normalize_WritesTheTypesInTheCatalogsOrder()
    {
        var types = ProjectSettingsCatalog.Default.TypeCodes.Reverse()
            .ToDictionary(code => code.ToUpperInvariant(), code => new ProjectTypeAppearance { Label = code });

        var check = ProjectSettingsRules.Normalize(new ProjectSettings { Types = types });

        check.Settings!.Types.Keys.ShouldBe(ProjectSettingsCatalog.Default.TypeCodes);
    }

    [Fact]
    public void Normalize_TwoCodesThatDifferOnlyInCase_AreRefused()
    {
        var input = new ProjectSettings
        {
            Types = new Dictionary<string, ProjectTypeAppearance>(StringComparer.Ordinal)
            {
                ["sbt"] = new() { Label = "Company" },
                ["SBT"] = new() { Label = "Firm" },
                ["Sbt"] = new() { Label = "Business" },
            },
        };

        var check = ProjectSettingsRules.Normalize(input);

        check.Settings.ShouldBeNull();
        check.Problems.Select(problem => (problem.Path, problem.Rule)).ShouldBe([("$.Types.sbt", ProjectSettingsRules.TypeCode)]);
    }

    [Fact]
    public void Normalize_ReportsEveryProblem()
    {
        var input = new ProjectSettings
        {
            Types = new Dictionary<string, ProjectTypeAppearance>
            {
                ["sbt"] = new() { Label = new string('x', 41), Plural = new string('y', 41), Color = "beige", Icon = "building-2" },
                ["cnt"] = new() { Plural = "Departments" },
                ["Subject"] = new() { Label = "Company" },
                ["my type"] = new() { Label = "Company" },
            },
            DefaultTimeZone = "Central Europe Standard Time",
            DateFormat = "dd.MM.yy",
        };

        var check = ProjectSettingsRules.Normalize(input);

        check.Settings.ShouldBeNull();
        check.Problems.Select(problem => (problem.Path, problem.Rule)).ShouldBe(
            [
                ("$.Types.Subject", ProjectSettingsRules.TypeCode),
                ("$.Types['my type']", ProjectSettingsRules.TypeCode),
                ("$.Types.sbt.Label", ProjectSettingsRules.Label),
                ("$.Types.sbt.Plural", ProjectSettingsRules.Plural),
                ("$.Types.sbt.Color", ProjectSettingsRules.Color),
                ("$.Types.sbt.Icon", ProjectSettingsRules.Icon),
                ("$.Types.cnt.Plural", ProjectSettingsRules.PluralWithoutLabel),
                ("$.DefaultTimeZone", ProjectSettingsRules.TimeZone),
                ("$.DateFormat", ProjectSettingsRules.DateFormat),
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void Normalize_NamesTheProblemInItsMessage()
    {
        var check = ProjectSettingsRules.Normalize(new ProjectSettings
        {
            Types = new Dictionary<string, ProjectTypeAppearance> { ["sbt"] = new() { Label = new string('x', 41), Color = "beige" } },
            DefaultTimeZone = "Mars/Base",
        });

        check.Problems.Single(problem => problem.Rule == ProjectSettingsRules.Label).Message.ShouldBe("The label has 41 characters; at most 40 are allowed.");
        check.Problems.Single(problem => problem.Rule == ProjectSettingsRules.Color).Message.ShouldStartWith("'beige' is not a color of the catalog; use one of red, orange,");
        check.Problems.Single(problem => problem.Rule == ProjectSettingsRules.TimeZone).Message.ShouldContain("'Mars/Base'");
    }

    // 40 code units: 20 characters outside the BMP fit, 21 do not.
    [Theory]
    [InlineData(20, true)]
    [InlineData(21, false)]
    public void Normalize_MeasuresLabelsInUtf16CodeUnits(int count, bool isValid)
    {
        var label = string.Concat(Enumerable.Repeat("\U0001F3E2", count));

        var check = ProjectSettingsRules.Normalize(new ProjectSettings
        {
            Types = new Dictionary<string, ProjectTypeAppearance> { ["sbt"] = new() { Label = label } },
        });

        check.IsValid.ShouldBe(isValid);
    }

    [Fact]
    public void Normalize_AppearanceWithoutValues_IsRemoved()
    {
        var check = ProjectSettingsRules.Normalize(new ProjectSettings
        {
            Types = new Dictionary<string, ProjectTypeAppearance> { ["sbt"] = new(), ["cnt"] = new() { Label = "　" }, ["rst"] = null! },
        });

        check.Settings!.Types.ShouldBeEmpty();
    }

    [Fact]
    public void SameValues_ComparesTheValuesOnly()
    {
        var settings = new ProjectSettings
        {
            Types = new Dictionary<string, ProjectTypeAppearance> { ["sbt"] = new() { Label = "Company", Color = "pink" } },
            DefaultTimeZone = "Europe/Prague",
            DateFormat = "dd.mm.yyyy",
            Version = 3,
            Modified = Stamp,
        };

        ProjectSettingsRules.SameValues(settings, settings with { Version = 4, Modified = null }).ShouldBeTrue();
        ProjectSettingsRules.SameValues(
                settings,
                settings with { Types = new Dictionary<string, ProjectTypeAppearance> { ["SBT"] = new() { Label = "Company", Color = "pink" }, ["cnt"] = new() } })
            .ShouldBeTrue();
        ProjectSettingsRules.SameValues(settings, settings with { DefaultTimeZone = null }).ShouldBeFalse();
        ProjectSettingsRules.SameValues(settings, settings with { DateFormat = "yyyy-mm-dd" }).ShouldBeFalse();
        ProjectSettingsRules.SameValues(
                settings,
                settings with { Types = new Dictionary<string, ProjectTypeAppearance> { ["sbt"] = new() { Label = "company", Color = "pink" } } })
            .ShouldBeFalse();
        ProjectSettingsRules.SameValues(
                settings,
                settings with { Types = new Dictionary<string, ProjectTypeAppearance> { ["sbt"] = new() { Label = "Company", Color = "pink" }, ["rst"] = new() { Icon = "i-lucide-box" } } })
            .ShouldBeFalse();
    }

    // Null is nothing stored, the defaults; UTC and yyyy-mm-dd given explicitly are values of their own.
    [Fact]
    public void SameValues_NullIsTheDefaults()
    {
        ProjectSettingsRules.SameValues(null, null).ShouldBeTrue();
        ProjectSettingsRules.SameValues(null, new ProjectSettings { Version = 5 }).ShouldBeTrue();
        ProjectSettingsRules.SameValues(new ProjectSettings(), null).ShouldBeTrue();
        ProjectSettingsRules.SameValues(null, new ProjectSettings { DefaultTimeZone = "UTC" }).ShouldBeFalse();
        ProjectSettingsRules.SameValues(null, new ProjectSettings { DateFormat = "yyyy-mm-dd" }).ShouldBeFalse();
    }

    [Fact]
    public void All_HoldsEveryRuleOnce()
    {
        ProjectSettingsRules.All.ShouldBeUnique();
        ProjectSettingsRules.All.ShouldBe(
            ["type-code", "label", "plural", "plural-without-label", "color", "icon", "time-zone", "date-format", "unknown-member", "case-collision", "type", "format"],
            ignoreOrder: true);
    }
}
