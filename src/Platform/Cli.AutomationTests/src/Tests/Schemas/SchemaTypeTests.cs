using _42.Platform.Cli.AutomationTests.Editing;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Schemas;

// Type Schemas: sform story schema type, the configuration schema of an annotation type in a view. A type schema applies to
// every configuration of the type in the view, so every case works in a disposable project of its own (mode P), where
// the schemas start at version 1.
[Area(Area.Schemas)]
public sealed class SchemaTypeTests : CliTest
{
    // SCH-TYP-01: schema type set -i stores a type schema, and schema type get returns it.
    // In a disposable project, story schema type set unt -i s.json stores the file as version 1 and prints it; set unt
    // --import s2.json replaces it with another document as version 2; schema type get unt returns s2.json as the Content of
    // version 2, of the type unt in the view of the run.
    [Test]
    [TestCase(247)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story schema type set", "arg0", "-i", "--import")]
    [Covers("story schema type get", "arg0")]
    public async Task SchemaType_SetGet()
    {
        await CreateDisposableProjectAsync();
        var first = SchemaChecks.ObjectSchema("first");
        var second = SchemaChecks.ObjectSchema("second");
        await WriteFileAsync("s.json", first);
        await WriteFileAsync("s2.json", second);

        (await Sform(InProject("story", "schema", "type", "set", "unt", "-i", "s.json"))).ShouldSucceed().ShouldPrintSchema(first, 1);
        (await Sform(InProject("story", "schema", "type", "set", "unt", "--import", "s2.json"))).ShouldSucceed().ShouldPrintSchema(second, 2);

        var stored = (await Sform(InProject("story", "schema", "type", "get", "unt"))).ShouldSucceed().ShouldPrintSchema(second, 2).JsonNode();
        await Assert.That(stored["AnnotationType"]?.GetValue<string>()).IsEqualTo("unt");
        await Assert.That(stored["View"]?.GetValue<string>()).IsEqualTo(View);
    }

    // SCH-TYP-02: schema type create is an alias, and a schema the configurations break fails without -f.
    // A unit <unt> of <rst> has the configuration {a: 1}. schema type create unt -i requires-req.json (a schema that
    // requires req) is refused with exit 2 and names <unt> (<view>), and stores nothing (get finds no schema); with -f it
    // is stored as version 1 with "Schema saved as version 1." and "Existing configurations were not required to
    // comply."; --force stores another breaking schema (requires-req2.json; the same file again would store no new
    // version) as version 2 with the same warning.
    [Test]
    [TestCase(264)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story schema type create", "-f", "--force")]
    public async Task SchemaType_Breaking_NeedsForce()
    {
        await CreateDisposableProjectAsync();
        var rst = CliData.ResponsibilityKey();
        var unt = CliData.UnitKeyOf(rst);
        (await Sform(InProject("story", "set", rst))).ShouldSucceed();
        (await Sform(InProject("story", "set", unt))).ShouldSucceed();
        (await Sform(InProject("story", "config", "set", unt, "-x", "a=1"))).ShouldSucceed();
        var requiresReq = SchemaChecks.RequiringSchema("req");
        var requiresReq2 = SchemaChecks.RequiringSchema("req2");
        await WriteFileAsync("requires-req.json", requiresReq);
        await WriteFileAsync("requires-req2.json", requiresReq2);

        (await Sform(InProject("story", "schema", "type", "create", "unt", "-i", "requires-req.json"))).ShouldRefuseForConfiguration(unt, View);
        (await Sform(InProject("story", "schema", "type", "get", "unt")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("The schema for 'unt' has not been found.");

        (await Sform(InProject("story", "schema", "type", "create", "unt", "-i", "requires-req.json", "-f")))
            .ShouldSucceed()
            .ShouldPrintSchema(requiresReq, 1)
            .ShouldBeForced(1);
        (await Sform(InProject("story", "schema", "type", "create", "unt", "-i", "requires-req2.json", "--force")))
            .ShouldSucceed()
            .ShouldPrintSchema(requiresReq2, 2)
            .ShouldBeForced(2);
    }

    // SCH-TYP-03: get --version, versions and diff with --format json and unified.
    // Three versions of the type schema of unt (each adds a property: first, second, third); schema type get unt
    // --version 1 returns the first; schema type versions unt lists 3, 2 and 1; schema type diff unt shows the JSON diff
    // of version 3 against 2 (third added), diff unt 3 1 --format unified a unified diff of 3 against 1, and diff unt 2 1
    // --format json the JSON diff of 2 against 1 (second added). For the unified diff sform reads the JSON diff and writes
    // the unified text itself: hunks "@@ -a,b +c,d @@" and +, - and space lines, raw on stdout.
    [Test]
    [TestCase(265)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story schema type get", "--version")]
    [Covers("story schema type versions", "arg0")]
    [Covers("story schema type diff", "arg0", "arg1", "arg2", "--format", "--format=json", "--format=unified")]
    public async Task SchemaType_History()
    {
        await CreateDisposableProjectAsync();
        string[] versions = [SchemaChecks.ObjectSchema("first"), SchemaChecks.ObjectSchema("first", "second"), SchemaChecks.ObjectSchema("first", "second", "third")];
        for (var index = 0; index < versions.Length; index++)
        {
            var file = $"v{index + 1}.json";
            await WriteFileAsync(file, versions[index]);
            (await Sform(InProject("story", "schema", "type", "set", "unt", "-i", file))).ShouldSucceed().ShouldHaveVersion(index + 1);
        }

        (await Sform(InProject("story", "schema", "type", "get", "unt", "--version", "1"))).ShouldSucceed().ShouldPrintSchema(versions[0], 1);
        (await Sform(InProject("story", "schema", "type", "versions", "unt")))
            .ShouldContain("Versions of the schema for 'unt'")
            .ShouldListVersions(3, 2, 1);
        (await Sform(InProject("story", "schema", "type", "diff", "unt"))).ShouldShowJsonDiff("third");
        var unified = await Sform(InProject("story", "schema", "type", "diff", "unt", "3", "1", "--format", "unified"));
        (await Sform(InProject("story", "schema", "type", "diff", "unt", "2", "1", "--format", "json"))).ShouldShowJsonDiff("second");
        unified.ShouldShowDiff("third");
    }

    // SCH-TYP-04: schema type edit -y stores the editor's change; -f for a breaking change.
    // A unit <unt> has the configuration {a: 1} and the type schema of unt is s.json (version 1). With the fake editor:
    // schema type edit unt -y with a property added saves "Schema for 'unt' has been saved (version 2)." (the editor got
    // the stored document); the same edit requiring req, which <unt> lacks, is refused with exit 2 and names <unt>; with -y
    // -f it is saved as version 3, and another breaking change with --yes --force as version 4, each with "Existing
    // configurations were not required to comply."; get returns version 4 with the added property and both requirements.
    [Test]
    [TestCase(266)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story schema type edit", "arg0", "-f", "--force", "-y", "--yes")]
    public async Task SchemaType_Edit()
    {
        await CreateDisposableProjectAsync();
        var rst = CliData.ResponsibilityKey();
        var unt = CliData.UnitKeyOf(rst);
        (await Sform(InProject("story", "set", rst))).ShouldSucceed();
        (await Sform(InProject("story", "set", unt))).ShouldSucceed();
        (await Sform(InProject("story", "config", "set", unt, "-x", "a=1"))).ShouldSucceed();
        var original = SchemaChecks.ObjectSchema("base");
        await WriteFileAsync("s.json", original);
        (await Sform(InProject("story", "schema", "type", "set", "unt", "-i", "s.json"))).ShouldSucceed().ShouldHaveVersion(1);
        var editor = await UseEditorAsync();

        var added = editor.With(EditorChange.Merge("""{ "properties": { "added": { "type": "string" } } }"""));
        var saved = (await Sform(added, InProject("story", "schema", "type", "edit", "unt", "-y")))
            .ShouldSucceed()
            .ShouldContain("Schema for 'unt' has been saved (version 2).");
        SchemaChecks.ShouldEqualJson(saved, editor.ReadLog(added).Single().OriginalJson(), original, "to the editor the document");

        var refused = editor.With(EditorChange.Merge("""{ "required": [ "req" ] }"""));
        (await Sform(refused, InProject("story", "schema", "type", "edit", "unt", "-y"))).ShouldRefuseForConfiguration(unt, View);

        var forced = editor.With(EditorChange.Merge("""{ "required": [ "req" ] }"""));
        (await Sform(forced, InProject("story", "schema", "type", "edit", "unt", "-y", "-f")))
            .ShouldSucceed()
            .ShouldContain("Schema for 'unt' has been saved (version 3).")
            .ShouldContain("Existing configurations were not required to comply.");

        var forcedAgain = editor.With(EditorChange.Merge("""{ "required": [ "req", "other" ] }"""));
        var last = (await Sform(forcedAgain, InProject("story", "schema", "type", "edit", "unt", "--yes", "--force")))
            .ShouldSucceed()
            .ShouldContain("Schema for 'unt' has been saved (version 4).")
            .ShouldContain("Existing configurations were not required to comply.");
        SchemaChecks.ShouldEqualJson(last, editor.ReadLog(forcedAgain).Single().OriginalJson()?["required"], """["req"]""", "to the editor the requirements");

        var stored = (await Sform(InProject("story", "schema", "type", "get", "unt"))).ShouldSucceed().ShouldHaveVersion(4).JsonNode()["Content"];
        await Assert.That(stored?["properties"]?["added"]).IsNotNull();
        SchemaChecks.ShouldEqualJson(last, stored?["required"], """["req","other"]""", "the stored requirements");
    }

    // SCH-TYP-05: schema type delete (and remove) deletes it.
    // Type schemas of unt and sbt (version 1 each); schema type delete unt and schema type remove sbt print "The schema for
    // 'unt' has been deleted." and "… 'sbt' …"; get then reports no schema for either, and versions unt and versions sbt
    // still list version 1.
    [Test]
    [TestCase(267)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story schema type delete", "arg0")]
    [Covers("story schema type remove", "arg0")]
    public async Task SchemaType_Delete()
    {
        await CreateDisposableProjectAsync();
        await WriteFileAsync("s.json", SchemaChecks.ObjectSchema("base"));
        (await Sform(InProject("story", "schema", "type", "set", "unt", "-i", "s.json"))).ShouldSucceed();
        (await Sform(InProject("story", "schema", "type", "set", "sbt", "-i", "s.json"))).ShouldSucceed();

        (await Sform(InProject("story", "schema", "type", "delete", "unt"))).ShouldSucceed().ShouldContain("The schema for 'unt' has been deleted.");
        (await Sform(InProject("story", "schema", "type", "remove", "sbt"))).ShouldSucceed().ShouldContain("The schema for 'sbt' has been deleted.");

        foreach (var code in new[] { "unt", "sbt" })
        {
            (await Sform(InProject("story", "schema", "type", "get", code)))
                .ShouldExitWith(CliExit.WrongInput)
                .ShouldContain($"The schema for '{code}' has not been found.");
            (await Sform(InProject("story", "schema", "type", "versions", code))).ShouldListVersions(1);
        }
    }

    // SCH-TYP-06: schema type restore --version stores an earlier version; configurations that break it need -f.
    // A unit <unt> of <rst>. The type schema of unt requires req (version 1), then does not (version 2), and <unt> gets the
    // configuration {a: 1}. schema type restore unt --version 1 ends with 2 and names <unt> (<view>), and version 2 stays
    // the current one; with -f it says "Restored v1 of the schema for 'unt' as v3."; --version 3 says "v3 is already the
    // current version of the schema for 'unt'." and ends with 42; --version 2 says "Restored v2 of the schema for 'unt' as
    // v4.", and --version 1 --force "Restored v1 of the schema for 'unt' as v5."; get then returns version 5 with the content
    // of version 1.
    [Test]
    [TestCase(491)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story schema type restore", "arg0", "--version", "-f", "--force")]
    public async Task SchemaType_Restore()
    {
        await CreateDisposableProjectAsync();
        var rst = CliData.ResponsibilityKey();
        var unt = CliData.UnitKeyOf(rst);
        (await Sform(InProject("story", "set", rst))).ShouldSucceed();
        (await Sform(InProject("story", "set", unt))).ShouldSucceed();
        var requiresReq = SchemaChecks.RequiringSchema("req");
        await WriteFileAsync("requires-req.json", requiresReq);
        await WriteFileAsync("s.json", SchemaChecks.ObjectSchema("req"));
        (await Sform(InProject("story", "schema", "type", "set", "unt", "-i", "requires-req.json"))).ShouldSucceed().ShouldHaveVersion(1);
        (await Sform(InProject("story", "schema", "type", "set", "unt", "-i", "s.json"))).ShouldSucceed().ShouldHaveVersion(2);
        (await Sform(InProject("story", "config", "set", unt, "-x", "a=1"))).ShouldSucceed();
        const string subject = "the schema for 'unt'";

        (await Sform(Restore("1"))).ShouldRefuseForConfiguration(unt, View);
        (await Sform(InProject("story", "schema", "type", "get", "unt"))).ShouldSucceed().ShouldHaveVersion(2);

        (await Sform(Restore("1", "-f"))).ShouldSucceed().ShouldContain($"Restored v1 of {subject} as v3.");
        (await Sform(Restore("3"))).ShouldExitWith(CliExit.NoWorkNeeded).ShouldContain($"v3 is already the current version of {subject}.");
        (await Sform(Restore("2"))).ShouldSucceed().ShouldContain($"Restored v2 of {subject} as v4.");
        (await Sform(Restore("1", "--force"))).ShouldSucceed().ShouldContain($"Restored v1 of {subject} as v5.");

        (await Sform(InProject("story", "schema", "type", "get", "unt"))).ShouldSucceed().ShouldPrintSchema(requiresReq, 5);

        string[] Restore(string version, params string[] options) => InProject(["story", "schema", "type", "restore", "unt", "--version", version, .. options]);
    }
}
