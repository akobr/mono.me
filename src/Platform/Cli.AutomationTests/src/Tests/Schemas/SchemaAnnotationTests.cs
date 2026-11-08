using _42.Platform.Cli.AutomationTests.Editing;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Schemas;

// Annotation Schemas: sform story schema annotation, the configuration schema of one annotation in a view. Every case
// works in a disposable project of its own (mode P), where the schemas start at version 1.
[Area(Area.Schemas)]
public sealed class SchemaAnnotationTests : CliTest
{
    // SCH-ANN-01: schema annotation set and get for one annotation.
    // With <rst> and <rst2>, schema annotation set <rst> -i s.json stores version 1, set <rst> --import s2.json (a schema
    // that requires req) version 2, and schema annotation get <rst> returns s2.json for the key <rst>. The schema applies
    // to <rst> only: config set <rst2> -x a=1 (without req) saves, while config set <rst> -x a=1 is refused with exit 2.
    [Test]
    [TestCase(277)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story schema annotation set", "arg0", "-i", "--import")]
    [Covers("story schema annotation get", "arg0")]
    public async Task SchemaAnnotation_SetGet()
    {
        await CreateDisposableProjectAsync();
        var rst = CliData.ResponsibilityKey();
        var other = CliData.ResponsibilityKey();
        (await Sform(InProject("story", "set", rst))).ShouldSucceed();
        (await Sform(InProject("story", "set", other))).ShouldSucceed();
        var first = SchemaChecks.ObjectSchema("first");
        var second = SchemaChecks.RequiringSchema("req");
        await WriteFileAsync("s.json", first);
        await WriteFileAsync("s2.json", second);

        (await Sform(InProject("story", "schema", "annotation", "set", rst, "-i", "s.json"))).ShouldSucceed().ShouldPrintSchema(first, 1);
        (await Sform(InProject("story", "schema", "annotation", "set", rst, "--import", "s2.json"))).ShouldSucceed().ShouldPrintSchema(second, 2);

        var stored = (await Sform(InProject("story", "schema", "annotation", "get", rst))).ShouldSucceed().ShouldPrintSchema(second, 2).JsonNode();
        await Assert.That(stored["AnnotationKey"]?.GetValue<string>()).IsEqualTo(rst);

        (await Sform(InProject("story", "config", "set", other, "-x", "a=1"))).ShouldSucceed();
        (await Sform(InProject("story", "config", "set", rst, "-x", "a=1"))).ShouldRefuseForConfiguration(rst, View);
    }

    // SCH-ANN-02: create alias, -f for configurations that break it.
    // <rst> has the configuration {a: 1}. schema annotation create <rst> -i requires-req.json is refused with exit 2 and
    // names <rst> (<view>), and stores nothing; with -f it is stored as version 1 with "Schema saved as version 1." and
    // "Existing configurations were not required to comply."; --force stores another breaking schema (requires-req2.json;
    // the same file again would store no new version) as version 2 with the same warning.
    [Test]
    [TestCase(280)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story schema annotation create", "-f", "--force")]
    public async Task SchemaAnnotation_Breaking_NeedsForce()
    {
        await CreateDisposableProjectAsync();
        var rst = CliData.ResponsibilityKey();
        (await Sform(InProject("story", "set", rst))).ShouldSucceed();
        (await Sform(InProject("story", "config", "set", rst, "-x", "a=1"))).ShouldSucceed();
        var requiresReq = SchemaChecks.RequiringSchema("req");
        var requiresReq2 = SchemaChecks.RequiringSchema("req2");
        await WriteFileAsync("requires-req.json", requiresReq);
        await WriteFileAsync("requires-req2.json", requiresReq2);

        (await Sform(InProject("story", "schema", "annotation", "create", rst, "-i", "requires-req.json"))).ShouldRefuseForConfiguration(rst, View);
        (await Sform(InProject("story", "schema", "annotation", "get", rst)))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"The schema for '{rst}' has not been found.");

        (await Sform(InProject("story", "schema", "annotation", "create", rst, "-i", "requires-req.json", "-f")))
            .ShouldSucceed()
            .ShouldPrintSchema(requiresReq, 1)
            .ShouldBeForced(1);
        (await Sform(InProject("story", "schema", "annotation", "create", rst, "-i", "requires-req2.json", "--force")))
            .ShouldSucceed()
            .ShouldPrintSchema(requiresReq2, 2)
            .ShouldBeForced(2);
    }

    // SCH-ANN-03: get --version, versions and diff with both formats.
    // Three versions of the schema of <rst> (each adds a property: first, second, third); get <rst> --version 1 returns the
    // first; versions <rst> lists 3, 2 and 1; diff <rst> shows the JSON diff of 3 against 2, diff <rst> 3 1 --format
    // unified a unified diff of 3 against 1, and diff <rst> 2 1 --format json the JSON diff of 2 against 1. The unified diff
    // is checked last (sform cannot read the API's text/plain answer yet, see SCH-TYP-03).
    [Test]
    [TestCase(282)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story schema annotation get", "--version")]
    [Covers("story schema annotation versions", "arg0")]
    [Covers("story schema annotation diff", "arg0", "arg1", "arg2", "--format", "--format=json", "--format=unified")]
    public async Task SchemaAnnotation_History()
    {
        await CreateDisposableProjectAsync();
        var rst = CliData.ResponsibilityKey();
        (await Sform(InProject("story", "set", rst))).ShouldSucceed();
        string[] versions = [SchemaChecks.ObjectSchema("first"), SchemaChecks.ObjectSchema("first", "second"), SchemaChecks.ObjectSchema("first", "second", "third")];
        for (var index = 0; index < versions.Length; index++)
        {
            var file = $"v{index + 1}.json";
            await WriteFileAsync(file, versions[index]);
            (await Sform(InProject("story", "schema", "annotation", "set", rst, "-i", file))).ShouldSucceed().ShouldHaveVersion(index + 1);
        }

        (await Sform(InProject("story", "schema", "annotation", "get", rst, "--version", "1"))).ShouldSucceed().ShouldPrintSchema(versions[0], 1);
        (await Sform(InProject("story", "schema", "annotation", "versions", rst)))
            .ShouldContain($"Versions of the schema for '{rst}'")
            .ShouldListVersions(3, 2, 1);
        (await Sform(InProject("story", "schema", "annotation", "diff", rst))).ShouldShowJsonDiff("third");
        var unified = await Sform(InProject("story", "schema", "annotation", "diff", rst, "3", "1", "--format", "unified"));
        (await Sform(InProject("story", "schema", "annotation", "diff", rst, "2", "1", "--format", "json"))).ShouldShowJsonDiff("second");
        unified.ShouldShowDiff("third");
    }

    // SCH-ANN-04: edit -y with the fake editor, -f for a breaking change.
    // <rst> has the configuration {a: 1} and the schema s.json (version 1). With the fake editor: schema annotation edit
    // <rst> -y with a property added saves version 2 ("Schema for '<rst>' has been saved (version 2)."; the editor got the
    // stored document); the same edit requiring req is refused with exit 2 and names <rst>; with -y -f it is saved as
    // version 3, and another breaking change with --yes --force as version 4, each with "Existing configurations were not
    // required to comply."; get returns version 4.
    [Test]
    [TestCase(285)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story schema annotation edit", "arg0", "-f", "--force", "-y", "--yes")]
    public async Task SchemaAnnotation_Edit()
    {
        await CreateDisposableProjectAsync();
        var rst = CliData.ResponsibilityKey();
        (await Sform(InProject("story", "set", rst))).ShouldSucceed();
        (await Sform(InProject("story", "config", "set", rst, "-x", "a=1"))).ShouldSucceed();
        var original = SchemaChecks.ObjectSchema("base");
        await WriteFileAsync("s.json", original);
        (await Sform(InProject("story", "schema", "annotation", "set", rst, "-i", "s.json"))).ShouldSucceed().ShouldHaveVersion(1);
        var editor = await UseEditorAsync();

        var added = editor.With(EditorChange.Merge("""{ "properties": { "added": { "type": "string" } } }"""));
        var saved = (await Sform(added, InProject("story", "schema", "annotation", "edit", rst, "-y")))
            .ShouldSucceed()
            .ShouldContain($"Schema for '{rst}' has been saved (version 2).");
        SchemaChecks.ShouldEqualJson(saved, editor.ReadLog(added).Single().OriginalJson(), original, "to the editor the document");

        var refused = editor.With(EditorChange.Merge("""{ "required": [ "req" ] }"""));
        (await Sform(refused, InProject("story", "schema", "annotation", "edit", rst, "-y"))).ShouldRefuseForConfiguration(rst, View);

        var forced = editor.With(EditorChange.Merge("""{ "required": [ "req" ] }"""));
        (await Sform(forced, InProject("story", "schema", "annotation", "edit", rst, "-y", "-f")))
            .ShouldSucceed()
            .ShouldContain($"Schema for '{rst}' has been saved (version 3).")
            .ShouldContain("Existing configurations were not required to comply.");

        var forcedAgain = editor.With(EditorChange.Merge("""{ "required": [ "req", "other" ] }"""));
        var last = (await Sform(forcedAgain, InProject("story", "schema", "annotation", "edit", rst, "--yes", "--force")))
            .ShouldSucceed()
            .ShouldContain($"Schema for '{rst}' has been saved (version 4).")
            .ShouldContain("Existing configurations were not required to comply.");

        var stored = (await Sform(InProject("story", "schema", "annotation", "get", rst))).ShouldSucceed().ShouldHaveVersion(4).JsonNode()["Content"];
        await Assert.That(stored?["properties"]?["added"]).IsNotNull();
        SchemaChecks.ShouldEqualJson(last, stored?["required"], """["req","other"]""", "the stored requirements");
    }

    // SCH-ANN-05: delete and remove.
    // Schemas of <rst> and <rst2> (version 1 each); schema annotation delete <rst> and schema annotation remove <rst2> print
    // "The schema for '<key>' has been deleted."; get then reports no schema for either, and versions still lists
    // version 1 of each.
    [Test]
    [TestCase(288)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story schema annotation delete", "arg0")]
    [Covers("story schema annotation remove", "arg0")]
    public async Task SchemaAnnotation_Delete()
    {
        await CreateDisposableProjectAsync();
        var rst = CliData.ResponsibilityKey();
        var rst2 = CliData.ResponsibilityKey();
        await WriteFileAsync("s.json", SchemaChecks.ObjectSchema("base"));
        foreach (var key in new[] { rst, rst2 })
        {
            (await Sform(InProject("story", "set", key))).ShouldSucceed();
            (await Sform(InProject("story", "schema", "annotation", "set", key, "-i", "s.json"))).ShouldSucceed();
        }

        (await Sform(InProject("story", "schema", "annotation", "delete", rst))).ShouldSucceed().ShouldContain($"The schema for '{rst}' has been deleted.");
        (await Sform(InProject("story", "schema", "annotation", "remove", rst2))).ShouldSucceed().ShouldContain($"The schema for '{rst2}' has been deleted.");

        foreach (var key in new[] { rst, rst2 })
        {
            (await Sform(InProject("story", "schema", "annotation", "get", key)))
                .ShouldExitWith(CliExit.WrongInput)
                .ShouldContain($"The schema for '{key}' has not been found.");
            (await Sform(InProject("story", "schema", "annotation", "versions", key))).ShouldListVersions(1);
        }
    }
}
