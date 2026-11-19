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
    public void ReadMergePatch_MergesIntoTheSettings()
    {
        var check = ProjectSettingsJson.ReadMergePatch(Settings, JsonNode.Parse("""
            { "types": { "SBT": { "color": "blue", "Plural": null }, "uxe": null, "cnt": { "Label": "Department" } }, "dateFormat": null, "Version": 3, "Modified": null }
            """)!.AsObject());

        check.Problems.ShouldBeEmpty();
        check.Settings!.Types.Keys.ShouldBe(["sbt", "cnt"], ignoreOrder: true);
        check.Settings.Types["sbt"].ShouldBe(new ProjectTypeAppearance { Label = "Company", Color = "blue", Icon = "i-lucide-building-2" });
        check.Settings.Types["cnt"].ShouldBe(new ProjectTypeAppearance { Label = "Department" });
        check.Settings.DefaultTimeZone.ShouldBe("Europe/Prague");
        check.Settings.DateFormat.ShouldBeNull();
        check.Settings.Version.ShouldBe(0);
        check.Settings.Modified.ShouldBeNull();
    }

    // Two names of the patch that differ only in case are refused as in a document, so neither the later one wins nor
    // a null one removes what the patch has just set; PUT refuses the same body by the same rule.
    [Theory]
    [InlineData("""{ "DateFormat": "dd.mm.yyyy", "dateFormat": "mm/dd/yyyy" }""", "$.DateFormat", ProjectSettingsRules.CaseCollision)]
    [InlineData("""{ "types": { "sbt": { "Color": "pink" } }, "Types": null }""", "$.Types", ProjectSettingsRules.CaseCollision)]
    [InlineData("""{ "Types": { "sbt": { "Color": "pink" }, "SBT": null } }""", "$.Types.sbt", ProjectSettingsRules.TypeCode)]
    [InlineData("""{ "Types": { "sbt": { "Color": "pink", "color": "blue" } } }""", "$.Types.sbt.Color", ProjectSettingsRules.CaseCollision)]
    public void ReadMergePatch_NamesThatDifferOnlyInCase_AreProblems(string patch, string path, string rule)
    {
        var parsed = JsonNode.Parse(patch)!.AsObject();
        var check = ProjectSettingsJson.ReadMergePatch(Settings, parsed);

        check.Settings.ShouldBeNull();
        check.Problems.Select(problem => (problem.Path, problem.Rule)).ShouldBe([(path, rule)]);
        ProjectSettingsJson.Read(parsed).Problems.Select(problem => (problem.Path, problem.Rule)).ShouldContain((path, rule));
    }

    // A name that is no member or no type code is refused also with null, which RFC 7396 alone would apply as nothing.
    [Theory]
    [InlineData("""{ "Theme": null }""", "$.Theme", ProjectSettingsRules.UnknownMember)]
    [InlineData("""{ "Types": { "xyz": null } }""", "$.Types.xyz", ProjectSettingsRules.TypeCode)]
    [InlineData("""{ "Types": { "sbt": { "Emoji": null } } }""", "$.Types.sbt.Emoji", ProjectSettingsRules.UnknownMember)]
    [InlineData("""{ "Types": { "rst": { "Emoji": "x" } } }""", "$.Types.rst.Emoji", ProjectSettingsRules.UnknownMember)]
    public void ReadMergePatch_NamesThatAreNoMembers_AreProblems(string patch, string path, string rule)
    {
        var check = ProjectSettingsJson.ReadMergePatch(Settings, JsonNode.Parse(patch)!.AsObject());

        check.Settings.ShouldBeNull();
        check.Problems.Select(problem => (problem.Path, problem.Rule)).ShouldBe([(path, rule)]);
    }

    [Fact]
    public void ReadMergePatch_ProblemsOfTheNamesAndOfTheResult_AreReportedTogether()
    {
        var check = ProjectSettingsJson.ReadMergePatch(Settings, JsonNode.Parse("""
            { "Types": { "sbt": { "Color": "pink" }, "SBT": null, "uxe": { "Icon": "i-lucide-banana" } }, "Theme": "dark", "DateFormat": "iso" }
            """)!.AsObject());

        check.Settings.ShouldBeNull();
        check.Problems.Select(problem => (problem.Path, problem.Rule)).ShouldBe(
            [
                ("$.Types.sbt", ProjectSettingsRules.TypeCode),
                ("$.Theme", ProjectSettingsRules.UnknownMember),
                ("$.Types.uxe.Icon", ProjectSettingsRules.Icon),
                ("$.DateFormat", ProjectSettingsRules.DateFormat),
            ],
            ignoreOrder: true);
    }

    [Theory]
    [InlineData("""{ "Types": { "sbt": { "Color": "blue" }, "SBT": null, "uxe": { "Color": "rose", "color": null } }, "Theme": null }""")]
    [InlineData("""{ "Types": { "sbt": { "Color": "blue", "Icon": null } }, "DefaultTimeZone": null }""")]
    public void ReadMergePatch_ChangesNoPatch(string text)
    {
        var patch = JsonNode.Parse(text)!.AsObject();

        ProjectSettingsJson.ReadMergePatch(Settings, patch);

        patch.ToJsonString().ShouldBe(JsonNode.Parse(text)!.ToJsonString());
    }

    [Fact]
    public void ApplyMergePatch_ThatRemovesEverything_IsTheDefaults()
    {
        var merged = ProjectSettingsJson.ApplyMergePatch(
            ProjectSettingsJson.ToJson(Settings, ProjectSettingsJsonShape.Api),
            JsonNode.Parse("""{ "Types": null, "DefaultTimeZone": null, "DateFormat": null }""")!.AsObject());

        var read = ProjectSettingsJson.Read(merged);
        read.Problems.ShouldBeEmpty();
        read.Settings.ShouldNotBeNull();
        ProjectSettingsRules.SameValues(read.Settings, null).ShouldBeTrue();
    }

    // A string value built in code, not parsed, is read as its text, without the quotes of its JSON.
    [Fact]
    public void Read_AStringValueBuiltInCode_IsItsText()
    {
        var label = System.Guid.Empty;
        var node = new JsonObject { ["Types"] = new JsonObject { ["sbt"] = new JsonObject { ["Label"] = JsonValue.Create(label) } } };

        var read = ProjectSettingsJson.Read(node);

        read.Problems.ShouldBeEmpty();
        read.Settings!.Types["sbt"].Label.ShouldBe(label.ToString());
    }

    // A parse of bytes lets an escape through that is no UTF-16 text; the read names it as a problem instead of throwing.
    [Fact]
    public void Read_ALoneSurrogate_IsAProblemOfItsMember()
    {
        var node = JsonNode.Parse(System.Text.Encoding.UTF8.GetBytes("""{ "Types": { "sbt": { "Label": "\ud800" } } }"""))!;

        var read = ProjectSettingsJson.Read(node);

        read.Settings.ShouldBeNull();
        var problem = read.Problems.ShouldHaveSingleItem();
        problem.Path.ShouldBe("$.Types.sbt.Label");
        problem.Rule.ShouldBe(ProjectSettingsRules.WrongType);
    }
}
