using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace _42.Platform.Storyteller.Backend.Core.UnitTests;

public class ProjectSettingsJsonTests
{
    // The text as written, + of an offset included.
    private static readonly JsonSerializerOptions Text = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly AuditStamp Stamp = AuditStamp.Of(AuditActor.Account("user-1", "Ada"), new DateTimeOffset(2026, 11, 15, 10, 0, 0, TimeSpan.Zero));

    private static readonly ProjectSettings Settings = new()
    {
        Types = new Dictionary<string, ProjectTypeAppearance>
        {
            ["uxe"] = new() { Label = "Job instance", Color = "gray" },
            ["sbt"] = new() { Label = "Company", Plural = "Companies", Color = "pink", Icon = "i-lucide-building-2" },
        },
        DefaultTimeZone = "Europe/Prague",
        DateFormat = "dd.mm.yyyy",
        Version = 7,
        Modified = Stamp,
    };

    // The exhibit data set's file, in whatever shape it is (the admin UI's camelCase, or the file shape).
    [Fact]
    public void Read_TheExhibitDataSetsFile_IsValid()
    {
        var check = ProjectSettingsJson.Read(ProjectSettingsFiles.Read(ProjectSettingsFiles.Exhibit));

        check.Problems.ShouldBeEmpty();
        check.Settings!.Types.Keys.ShouldBe(ProjectSettingsCatalog.Default.TypeCodes);
        check.Settings.Types["sbt"].ShouldBe(new ProjectTypeAppearance { Label = "Company", Plural = "Companies", Color = "pink", Icon = "i-lucide-building-2" });
        check.Settings.DefaultTimeZone.ShouldBe("America/Phoenix");
        check.Settings.DateFormat.ShouldBe("yyyy-mm-dd");
    }

    [Fact]
    public void Read_MembersInAnyCase_AreTheCanonicalOnes()
    {
        var check = ProjectSettingsJson.Read(JsonNode.Parse("""
            { "TYPES": { "Sbt": { "lAbEl": "Company", "color": "pink" } }, "defaultTimeZone": "europe/prague", "dateformat": "d MMM yyyy", "format": "2s.project-settings.v1" }
            """));

        check.Problems.ShouldBeEmpty();
        check.Settings!.Types["sbt"].ShouldBe(new ProjectTypeAppearance { Label = "Company", Color = "pink" });
        check.Settings.DefaultTimeZone.ShouldBe("Europe/Prague");
        check.Settings.DateFormat.ShouldBe("d mmm yyyy");
    }

    [Fact]
    public void Read_IgnoresVersionAndModified()
    {
        var check = ProjectSettingsJson.Read(ProjectSettingsJson.ToJson(Settings, ProjectSettingsJsonShape.Api));

        check.Problems.ShouldBeEmpty();
        check.Settings!.Version.ShouldBe(0);
        check.Settings.Modified.ShouldBeNull();
        ProjectSettingsRules.SameValues(check.Settings, Settings).ShouldBeTrue();
    }

    [Fact]
    public void Read_UnknownMembers_AreProblems()
    {
        var check = ProjectSettingsJson.Read(JsonNode.Parse("""
            { "Types": { "sbt": { "Label": "Company", "Emoji": "x" } }, "Theme": "dark", "time zone": "UTC", "Types2": {} }
            """));

        check.Settings.ShouldBeNull();
        check.Problems.Select(problem => (problem.Path, problem.Rule)).ShouldBe(
            [
                ("$.Theme", ProjectSettingsRules.UnknownMember),
                ("$['time zone']", ProjectSettingsRules.UnknownMember),
                ("$.Types2", ProjectSettingsRules.UnknownMember),
                ("$.Types.sbt.Emoji", ProjectSettingsRules.UnknownMember),
            ],
            ignoreOrder: true);
        check.Problems[0].Message.ShouldBe("'Theme' is not a member; the members are Types, DefaultTimeZone and DateFormat (and the read-only Format, Version and Modified).");
    }

    [Fact]
    public void Read_ProblemsOfTheShapeAndOfTheValues_AreReportedTogether()
    {
        var check = ProjectSettingsJson.Read(JsonNode.Parse("""
            { "Types": { "sbt": { "Color": 3, "Icon": "i-lucide-banana" } }, "DateFormat": "iso", "Format": "v2" }
            """));

        check.Problems.Select(problem => (problem.Path, problem.Rule)).ShouldBe(
            [
                ("$.Format", ProjectSettingsRules.Format),
                ("$.Types.sbt.Color", ProjectSettingsRules.WrongType),
                ("$.Types.sbt.Icon", ProjectSettingsRules.Icon),
                ("$.DateFormat", ProjectSettingsRules.DateFormat),
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void ToJson_Api_IsTheCanonicalShape()
    {
        ProjectSettingsJson.ToJson(Settings, ProjectSettingsJsonShape.Api).ToJsonString(Text).ShouldBe(
            """{"Types":{"sbt":{"Label":"Company","Plural":"Companies","Color":"pink","Icon":"i-lucide-building-2"},"uxe":{"Label":"Job instance","Color":"gray"}},"DefaultTimeZone":"Europe/Prague","DateFormat":"dd.mm.yyyy","Version":7,"Modified":{"At":"2026-11-15T10:00:00+00:00","By":{"Kind":"Account","Id":"user-1","Name":"Ada"}}}""");
    }

    [Fact]
    public void ToJson_File_HasTheMarkerAndNoVersion()
    {
        ProjectSettingsJson.ToJson(Settings, ProjectSettingsJsonShape.File).ToJsonString().ShouldBe(
            """{"Format":"2s.project-settings.v1","Types":{"sbt":{"Label":"Company","Plural":"Companies","Color":"pink","Icon":"i-lucide-building-2"},"uxe":{"Label":"Job instance","Color":"gray"}},"DefaultTimeZone":"Europe/Prague","DateFormat":"dd.mm.yyyy"}""");
    }

    [Fact]
    public void ToJson_TheDefaults_HaveEmptyTypes()
    {
        ProjectSettingsJson.ToJson(new ProjectSettings(), ProjectSettingsJsonShape.Api).ToJsonString().ShouldBe("""{"Types":{},"Version":0}""");
        ProjectSettingsJson.ToJson(new ProjectSettings(), ProjectSettingsJsonShape.File).ToJsonString().ShouldBe("""{"Format":"2s.project-settings.v1","Types":{}}""");
    }

    [Fact]
    public void ApplyMergePatch_NullRemovesAndObjectsMerge()
    {
        var target = ProjectSettingsJson.ToJson(Settings, ProjectSettingsJsonShape.Api);
        var patch = JsonNode.Parse("""
            { "types": { "SBT": { "color": "blue", "Plural": null }, "uxe": null, "cnt": { "Label": "Department", "Icon": null } }, "dateFormat": null }
            """)!.AsObject();

        var merged = ProjectSettingsJson.ApplyMergePatch(target, patch);
        var check = ProjectSettingsJson.Read(merged);

        check.Problems.ShouldBeEmpty();
        check.Settings!.Types.Keys.ShouldBe(["sbt", "cnt"]);
        check.Settings.Types["sbt"].ShouldBe(new ProjectTypeAppearance { Label = "Company", Color = "blue", Icon = "i-lucide-building-2" });
        check.Settings.Types["cnt"].ShouldBe(new ProjectTypeAppearance { Label = "Department" });
        check.Settings.DefaultTimeZone.ShouldBe("Europe/Prague");
        check.Settings.DateFormat.ShouldBeNull();

        // The target's spelling stays; a member new to the target keeps the patch's.
        merged.Select(member => member.Key).ShouldBe(["Types", "DefaultTimeZone", "Version", "Modified"]);
        merged["Types"]!.AsObject().Select(member => member.Key).ShouldBe(["sbt", "cnt"]);
        merged["Types"]!["sbt"]!.AsObject().Select(member => member.Key).ShouldBe(["Label", "Color", "Icon"]);
    }

    [Fact]
    public void ApplyMergePatch_ChangesNeitherInput()
    {
        var target = ProjectSettingsJson.ToJson(Settings, ProjectSettingsJsonShape.Api);
        var patch = JsonNode.Parse("""{ "Types": { "sbt": { "Color": "blue" } }, "DefaultTimeZone": null }""")!.AsObject();
        var targetBefore = target.ToJsonString();
        var patchBefore = patch.ToJsonString();

        var merged = ProjectSettingsJson.ApplyMergePatch(target, patch);
        merged["Types"]!["sbt"]!["Label"] = "Changed";

        target.ToJsonString().ShouldBe(targetBefore);
        patch.ToJsonString().ShouldBe(patchBefore);
    }

    // RFC 7396: a value that is not an object replaces, and an object merged into nothing loses its nulls.
    [Theory]
    [InlineData("""{ "Types": { "sbt": { "Label": "A" } } }""", """{ "Types": [] }""", """{"Types":[]}""")]
    [InlineData("""{ "Types": [] }""", """{ "Types": { "sbt": { "Label": "B", "Color": null } } }""", """{"Types":{"sbt":{"Label":"B"}}}""")]
    [InlineData("""{ "Types": { "sbt": { "Label": "A" } } }""", """{ "Types": { "sbt": "x" } }""", """{"Types":{"sbt":"x"}}""")]
    [InlineData("""{ "DateFormat": "dd.mm.yyyy", "dateformat": "yyyy-mm-dd" }""", """{ "DATEFORMAT": null }""", """{}""")]
    [InlineData("""{ "DateFormat": "dd.mm.yyyy", "dateformat": "yyyy-mm-dd" }""", """{ "DATEFORMAT": "d mmm yyyy" }""", """{"DateFormat":"d mmm yyyy"}""")]
    [InlineData("""{}""", """{ "Types": null, "DefaultTimeZone": "UTC" }""", """{"DefaultTimeZone":"UTC"}""")]
    public void ApplyMergePatch_FollowsRfc7396(string target, string patch, string expected)
    {
        ProjectSettingsJson.ApplyMergePatch(JsonNode.Parse(target)!.AsObject(), JsonNode.Parse(patch)!.AsObject())
            .ToJsonString().ShouldBe(expected);
    }

    [Fact]
    public void ApplyMergePatch_ThatRemovesEverything_IsTheDefaults()
    {
        var merged = ProjectSettingsJson.ApplyMergePatch(
            ProjectSettingsJson.ToJson(Settings, ProjectSettingsJsonShape.Api),
            JsonNode.Parse("""{ "Types": null, "DefaultTimeZone": null, "DateFormat": null }""")!.AsObject());

        ProjectSettingsRules.SameValues(ProjectSettingsJson.Read(merged).Settings, null).ShouldBeTrue();
    }
}
