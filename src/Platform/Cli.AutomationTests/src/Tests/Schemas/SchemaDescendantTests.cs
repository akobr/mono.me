using _42.Platform.Cli.AutomationTests.Editing;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Schemas;

// Descendant Schemas: sform story schema descendant, the configuration schema of the annotations of a type below an
// ancestor (unt below <rst>) in a view. Every case works in a disposable project of its own (mode P), where the schemas
// start at version 1.
[Area(Area.Schemas)]
public sealed class SchemaDescendantTests : CliTest
{
    // SCH-DSC-01: schema descendant set and get for a type below an ancestor.
    // With <rst> and its unit <unt>, and <rst2> and its unit <unt2>: schema descendant set <rst> unt -i s.json stores
    // version 1, set <rst> unt --import s2.json (a schema that requires req) version 2, and schema descendant get <rst> unt
    // returns s2.json for <rst> and unt. It applies to the units below <rst> only: config set <unt2> -x a=1 (without req)
    // saves, while config set <unt> -x a=1 is refused with exit 2.
    [Test]
    [TestCase(299)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story schema descendant set", "arg0", "arg1", "-i", "--import")]
    [Covers("story schema descendant get", "arg0", "arg1")]
    public async Task SchemaDescendant_SetGet()
    {
        await CreateDisposableProjectAsync();
        var rst = CliData.ResponsibilityKey();
        var unt = CliData.UnitKeyOf(rst);
        var rst2 = CliData.ResponsibilityKey();
        var unt2 = CliData.UnitKeyOf(rst2);
        foreach (var key in new[] { rst, unt, rst2, unt2 })
        {
            (await Sform(InProject("story", "set", key))).ShouldSucceed();
        }

        var first = SchemaChecks.ObjectSchema("first");
        var second = SchemaChecks.RequiringSchema("req");
        await WriteFileAsync("s.json", first);
        await WriteFileAsync("s2.json", second);

        (await Sform(InProject("story", "schema", "descendant", "set", rst, "unt", "-i", "s.json"))).ShouldSucceed().ShouldPrintSchema(first, 1);
        (await Sform(InProject("story", "schema", "descendant", "set", rst, "unt", "--import", "s2.json"))).ShouldSucceed().ShouldPrintSchema(second, 2);

        var stored = (await Sform(InProject("story", "schema", "descendant", "get", rst, "unt"))).ShouldSucceed().ShouldPrintSchema(second, 2).JsonNode();
        await Assert.That(stored["AnnotationKey"]?.GetValue<string>()).IsEqualTo(rst);
        await Assert.That(stored["AnnotationType"]?.GetValue<string>()).IsEqualTo("unt");

        (await Sform(InProject("story", "config", "set", unt2, "-x", "a=1"))).ShouldSucceed();
        (await Sform(InProject("story", "config", "set", unt, "-x", "a=1"))).ShouldRefuseForConfiguration(unt, View);
    }

    // SCH-DSC-02: A type that cannot descend from the ancestor fails with InvalidKey.
    // schema descendant set <rst> sbt -i s.json (a subject is no descendant of a responsibility) ends with exit 2, the
    // server's "The type sbt cannot descend from <rst>, …" and "Error code: InvalidKey".
    [Test]
    [TestCase(300)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story schema descendant set", "arg1")]
    public async Task SchemaDescendant_InvalidType_Fails()
    {
        await CreateDisposableProjectAsync();
        var rst = CliData.ResponsibilityKey();
        (await Sform(InProject("story", "set", rst))).ShouldSucceed();
        await WriteFileAsync("s.json", SchemaChecks.ObjectSchema("base"));

        (await Sform(InProject("story", "schema", "descendant", "set", rst, "sbt", "-i", "s.json")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"The type sbt cannot descend from {rst}")
            .ShouldContain("Error code: InvalidKey");
    }

    // SCH-DSC-03: create alias, -f, get --version, versions and diff with both formats.
    // A unit <unt> of <rst> has the configuration {a: 1}. schema descendant create <rst> unt -i requires-req.json is
    // refused with exit 2 and names <unt> (<view>); set <rst> unt -i requires-req.json -f stores version 1, set … -i
    // requires-req2.json --force version 2, set … -i s3.json -f version 3; get <rst> unt --version 1 returns the first,
    // versions <rst> unt lists 3, 2 and 1, diff <rst> unt shows the JSON diff of 3 against 2, diff <rst> unt 3 1 --format
    // unified a unified diff of 3 against 1, and diff <rst> unt 2 1 --format json the JSON diff of 2 against 1 (the unified
    // text as in SCH-TYP-03).
    [Test]
    [TestCase(301)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story schema descendant create")]
    [Covers("story schema descendant set", "-f", "--force")]
    [Covers("story schema descendant get", "--version")]
    [Covers("story schema descendant versions", "arg0", "arg1")]
    [Covers("story schema descendant diff", "arg0", "arg1", "arg2", "arg3", "--format", "--format=json", "--format=unified")]
    public async Task SchemaDescendant_History()
    {
        await CreateDisposableProjectAsync();
        var rst = CliData.ResponsibilityKey();
        var unt = CliData.UnitKeyOf(rst);
        (await Sform(InProject("story", "set", rst))).ShouldSucceed();
        (await Sform(InProject("story", "set", unt))).ShouldSucceed();
        (await Sform(InProject("story", "config", "set", unt, "-x", "a=1"))).ShouldSucceed();
        var requiresReq = SchemaChecks.RequiringSchema("req");
        await WriteFileAsync("requires-req.json", requiresReq);
        await WriteFileAsync("requires-req2.json", SchemaChecks.RequiringSchema("req2"));
        await WriteFileAsync("s3.json", SchemaChecks.RequiringSchema("req2", "third"));

        (await Sform(InProject("story", "schema", "descendant", "create", rst, "unt", "-i", "requires-req.json"))).ShouldRefuseForConfiguration(unt, View);
        (await Sform(InProject("story", "schema", "descendant", "set", rst, "unt", "-i", "requires-req.json", "-f"))).ShouldSucceed().ShouldPrintSchema(requiresReq, 1).ShouldBeForced(1);
        (await Sform(InProject("story", "schema", "descendant", "set", rst, "unt", "-i", "requires-req2.json", "--force"))).ShouldSucceed().ShouldHaveVersion(2).ShouldBeForced(2);
        (await Sform(InProject("story", "schema", "descendant", "set", rst, "unt", "-i", "s3.json", "-f"))).ShouldSucceed().ShouldHaveVersion(3).ShouldBeForced(3);

        (await Sform(InProject("story", "schema", "descendant", "get", rst, "unt", "--version", "1"))).ShouldSucceed().ShouldPrintSchema(requiresReq, 1);
        (await Sform(InProject("story", "schema", "descendant", "versions", rst, "unt")))
            .ShouldContain($"Versions of the schema for 'unt' under '{rst}'")
            .ShouldListVersions(3, 2, 1);
        (await Sform(InProject("story", "schema", "descendant", "diff", rst, "unt"))).ShouldShowJsonDiff("third");
        var unified = await Sform(InProject("story", "schema", "descendant", "diff", rst, "unt", "3", "1", "--format", "unified"));
        (await Sform(InProject("story", "schema", "descendant", "diff", rst, "unt", "2", "1", "--format", "json"))).ShouldShowJsonDiff("req2");
        unified.ShouldShowDiff("third");
    }

    // SCH-DSC-04: edit -y with the fake editor, -f for a breaking change.
    // A unit <unt> of <rst> has the configuration {a: 1}, and the descendant schema of unt below <rst> is s.json
    // (version 1). With the fake editor: schema descendant edit <rst> unt -y with a property added saves version 2
    // ("Schema for 'unt' under '<rst>' has been saved (version 2)."; the editor got the stored document); the same edit
    // requiring req is refused with exit 2 and names <unt>; with -y -f it is saved as version 3, and another breaking
    // change with --yes --force as version 4, each with "Existing configurations were not required to comply."; get
    // returns version 4.
    [Test]
    [TestCase(302)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story schema descendant edit", "arg0", "arg1", "-f", "--force", "-y", "--yes")]
    public async Task SchemaDescendant_Edit()
    {
        await CreateDisposableProjectAsync();
        var rst = CliData.ResponsibilityKey();
        var unt = CliData.UnitKeyOf(rst);
        (await Sform(InProject("story", "set", rst))).ShouldSucceed();
        (await Sform(InProject("story", "set", unt))).ShouldSucceed();
        (await Sform(InProject("story", "config", "set", unt, "-x", "a=1"))).ShouldSucceed();
        var original = SchemaChecks.ObjectSchema("base");
        await WriteFileAsync("s.json", original);
        (await Sform(InProject("story", "schema", "descendant", "set", rst, "unt", "-i", "s.json"))).ShouldSucceed().ShouldHaveVersion(1);
        var editor = await UseEditorAsync();
        var subject = $"'unt' under '{rst}'";

        var added = editor.With(EditorChange.Merge("""{ "properties": { "added": { "type": "string" } } }"""));
        var saved = (await Sform(added, InProject("story", "schema", "descendant", "edit", rst, "unt", "-y")))
            .ShouldSucceed()
            .ShouldContain($"Schema for {subject} has been saved (version 2).");
        SchemaChecks.ShouldEqualJson(saved, editor.ReadLog(added).Single().OriginalJson(), original, "to the editor the document");

        var refused = editor.With(EditorChange.Merge("""{ "required": [ "req" ] }"""));
        (await Sform(refused, InProject("story", "schema", "descendant", "edit", rst, "unt", "-y"))).ShouldRefuseForConfiguration(unt, View);

        var forced = editor.With(EditorChange.Merge("""{ "required": [ "req" ] }"""));
        (await Sform(forced, InProject("story", "schema", "descendant", "edit", rst, "unt", "-y", "-f")))
            .ShouldSucceed()
            .ShouldContain($"Schema for {subject} has been saved (version 3).")
            .ShouldContain("Existing configurations were not required to comply.");

        var forcedAgain = editor.With(EditorChange.Merge("""{ "required": [ "req", "other" ] }"""));
        var last = (await Sform(forcedAgain, InProject("story", "schema", "descendant", "edit", rst, "unt", "--yes", "--force")))
            .ShouldSucceed()
            .ShouldContain($"Schema for {subject} has been saved (version 4).")
            .ShouldContain("Existing configurations were not required to comply.");

        var stored = (await Sform(InProject("story", "schema", "descendant", "get", rst, "unt"))).ShouldSucceed().ShouldHaveVersion(4).JsonNode()["Content"];
        await Assert.That(stored?["properties"]?["added"]).IsNotNull();
        SchemaChecks.ShouldEqualJson(last, stored?["required"], """["req","other"]""", "the stored requirements");
    }

    // SCH-DSC-05: delete and remove.
    // Descendant schemas of unt and usg below <rst> (version 1 each); schema descendant delete <rst> unt and schema
    // descendant remove <rst> usg print "The schema for 'unt' under '<rst>' has been deleted." (and usg); get then reports
    // no current schema for either, and versions still lists version 1 of each.
    [Test]
    [TestCase(303)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story schema descendant delete", "arg0", "arg1")]
    [Covers("story schema descendant remove", "arg0", "arg1")]
    public async Task SchemaDescendant_Delete()
    {
        await CreateDisposableProjectAsync();
        var rst = CliData.ResponsibilityKey();
        (await Sform(InProject("story", "set", rst))).ShouldSucceed();
        await WriteFileAsync("s.json", SchemaChecks.ObjectSchema("base"));
        foreach (var code in new[] { "unt", "usg" })
        {
            (await Sform(InProject("story", "schema", "descendant", "set", rst, code, "-i", "s.json"))).ShouldSucceed();
        }

        (await Sform(InProject("story", "schema", "descendant", "delete", rst, "unt"))).ShouldSucceed().ShouldContain($"The schema for 'unt' under '{rst}' has been deleted.");
        (await Sform(InProject("story", "schema", "descendant", "remove", rst, "usg"))).ShouldSucceed().ShouldContain($"The schema for 'usg' under '{rst}' has been deleted.");

        foreach (var code in new[] { "unt", "usg" })
        {
            (await Sform(InProject("story", "schema", "descendant", "get", rst, code)))
                .ShouldExitWith(CliExit.WrongInput)
                .ShouldContain($"The schema for '{code}' under '{rst}' has not been found.");
            (await Sform(InProject("story", "schema", "descendant", "versions", rst, code))).ShouldListVersions(1);
        }
    }
}
