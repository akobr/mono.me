using System.Text.Json.Nodes;
using _42.Platform.Cli.AutomationTests.Editing;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Schemas;

// Context: the context options of every schema command (--projectKey, -v, --view), the context case of the section; -p
// counts with each command's own cases.
[Area(Area.Schemas)]
public sealed class SchemaContextTests : CliTest
{
    // SCH-CTX-01: Every schema command takes its project from --projectKey and its view from -v or --view.
    // <A> (Target:Project, Target:View) is the default project (account set); <B> a disposable project with <rst1>, <rst2>
    // and a unit <unt1> of <rst1> in a new view <view>. For the type schemas of unt and sbt, the schemas of <rst1> and
    // <rst2>, and the descendant schemas of unt and usg below <rst1>, each with --projectKey <B> and --view <view> (the
    // first of each pair) or -v <view> (the second): set -i s.json stores version 1 there, get returns it, edit -y (the fake
    // editor adds a property of its own) saves version 2 from the document of <view> in <B>, versions lists 2 and 1, diff
    // shows the added property; schema definition <unt1> (with --view, then -v) combines the type schema of unt and the
    // descendant schema below <rst1> with both added properties; the deletes delete each. The view default of <B> has no
    // version of any of these schemas, <A> none of <rst1>'s, and the versions of the type schemas of unt and sbt in <A> are
    // the same as before the steps.
    [Test]
    [TestCase(336)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story schema type set", "--projectKey", "-v", "--view")]
    [Covers("story schema type get", "--projectKey", "-v", "--view")]
    [Covers("story schema type edit", "--projectKey", "-v", "--view")]
    [Covers("story schema type versions", "--projectKey", "-v", "--view")]
    [Covers("story schema type diff", "--projectKey", "-v", "--view")]
    [Covers("story schema annotation set", "--projectKey", "-v", "--view")]
    [Covers("story schema annotation get", "--projectKey", "-v", "--view")]
    [Covers("story schema annotation edit", "--projectKey", "-v", "--view")]
    [Covers("story schema annotation versions", "--projectKey", "-v", "--view")]
    [Covers("story schema annotation diff", "--projectKey", "-v", "--view")]
    [Covers("story schema descendant set", "--projectKey", "-v", "--view")]
    [Covers("story schema descendant get", "--projectKey", "-v", "--view")]
    [Covers("story schema descendant edit", "--projectKey", "-v", "--view")]
    [Covers("story schema descendant versions", "--projectKey", "-v", "--view")]
    [Covers("story schema descendant diff", "--projectKey", "-v", "--view")]
    [Covers("story schema definition", "--projectKey", "-v", "--view")]
    [Covers("story schema type delete", "--projectKey", "-v", "--view")]
    [Covers("story schema annotation delete", "--projectKey", "-v", "--view")]
    [Covers("story schema descendant delete", "--projectKey", "-v", "--view")]
    public async Task SchemaCommands_ContextOptions_TargetProjectAndView()
    {
        var b = await CreateDisposableProjectAsync();
        var view = CliData.ViewName();
        var rst1 = CliData.ResponsibilityKey();
        var rst2 = CliData.ResponsibilityKey();
        var unt1 = CliData.UnitKeyOf(rst1);
        foreach (var key in new[] { rst1, rst2, unt1 })
        {
            (await Sform("story", "set", key, "-p", b, "-v", view)).ShouldSucceed();
        }

        var a = Target.ProjectKey;
        var aView = Target.View;
        (await Sform("account", "set", "-p", a, "-v", aView)).ShouldSucceed();
        var schema = SchemaChecks.ObjectSchema("base");
        await WriteFileAsync("s.json", schema);
        var editor = await UseEditorAsync();

        Subject[] subjects =
        [
            new("type", ["unt"], "'unt'", "--view", "ctx_type_unt"),
            new("type", ["sbt"], "'sbt'", "-v", "ctx_type_sbt"),
            new("annotation", [rst1], $"'{rst1}'", "--view", "ctx_annotation_1"),
            new("annotation", [rst2], $"'{rst2}'", "-v", "ctx_annotation_2"),
            new("descendant", [rst1, "unt"], $"'unt' under '{rst1}'", "--view", "ctx_descendant_unt"),
            new("descendant", [rst1, "usg"], $"'usg' under '{rst1}'", "-v", "ctx_descendant_usg"),
        ];

        var typesOfA = new Dictionary<string, IReadOnlyList<long>>(StringComparer.Ordinal);
        foreach (var code in new[] { "unt", "sbt" })
        {
            typesOfA[code] = (await Sform("story", "schema", "type", "versions", code, "-p", a, "-v", aView)).ShouldSucceed().VersionRows(SchemaChecks.VersionsHeader);
        }

        foreach (var layer in subjects.GroupBy(subject => subject.Layer))
        {
            foreach (var subject in layer)
            {
                var set = (await Sform(subject.Command(b, view, "set", "-i", "s.json"))).ShouldSucceed().ShouldPrintSchema(schema, 1);
                await Assert.That(set.JsonNode()["View"]?.GetValue<string>()).IsEqualTo(view);
            }

            foreach (var subject in layer)
            {
                var get = (await Sform(subject.Command(b, view, "get"))).ShouldSucceed().ShouldPrintSchema(schema, 1);
                await Assert.That(get.JsonNode()["View"]?.GetValue<string>()).IsEqualTo(view);
            }

            foreach (var subject in layer)
            {
                var options = editor.With(EditorChange.Merge(new JsonObject { ["properties"] = new JsonObject { [subject.Added] = new JsonObject { ["type"] = "string" } } }));
                var edit = (await Sform(options, subject.Command(b, view, "edit", "-y")))
                    .ShouldSucceed()
                    .ShouldContain($"Schema for {subject.Description} has been saved (version 2).");
                SchemaChecks.ShouldEqualJson(edit, editor.ReadLog(options).Single().OriginalJson(), schema, "to the editor the document");
            }

            foreach (var subject in layer)
            {
                (await Sform(subject.Command(b, view, "versions"))).ShouldListVersions(2, 1);
            }

            foreach (var subject in layer)
            {
                (await Sform(subject.Command(b, view, "diff"))).ShouldShowJsonDiff(subject.Added);
            }
        }

        foreach (var viewOption in new[] { "--view", "-v" })
        {
            var definition = (await Sform("story", "schema", "definition", unt1, "--projectKey", b, viewOption, view)).ShouldSucceed();
            var combined = definition.JsonNode();
            await Assert.That(combined["View"]?.GetValue<string>()).IsEqualTo(view);
            await Assert.That((combined["AppliedSchemas"] as JsonArray)?.Count).IsEqualTo(2);
            await Assert.That(combined["MergedContent"]?["properties"]?["ctx_type_unt"]).IsNotNull();
            await Assert.That(combined["MergedContent"]?["properties"]?["ctx_descendant_unt"]).IsNotNull();
        }

        foreach (var subject in subjects)
        {
            (await Sform(subject.Command(b, view, "delete"))).ShouldSucceed().ShouldContain($"The schema for {subject.Description} has been deleted.");
        }

        foreach (var subject in subjects)
        {
            (await Sform(["story", "schema", subject.Layer, "versions", .. subject.Arguments, "-p", b, "-v", "default"])).ShouldListVersions();
        }

        foreach (var subject in subjects.Where(subject => subject.Layer != "type" && subject.Arguments[0] == rst1))
        {
            (await Sform(["story", "schema", subject.Layer, "versions", .. subject.Arguments, "-p", a, "-v", aView])).ShouldListVersions();
        }

        foreach (var (code, before) in typesOfA)
        {
            (await Sform("story", "schema", "type", "versions", code, "-p", a, "-v", aView)).ShouldListVersions([.. before]);
        }
    }

    // One schema of the steps: its layer (type, annotation, descendant), its arguments, how sform names it, the form of the
    // view option its commands use, and the property the fake editor adds to it.
    private sealed record Subject(string Layer, string[] Arguments, string Description, string ViewOption, string Added)
    {
        // story schema <layer> <verb> <arguments> <options> --projectKey <project> <view option> <view>
        public string[] Command(string projectKey, string view, string verb, params string[] options)
        {
            return ["story", "schema", Layer, verb, .. Arguments, .. options, "--projectKey", projectKey, ViewOption, view];
        }
    }
}
