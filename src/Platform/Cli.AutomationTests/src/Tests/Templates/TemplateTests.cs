using System.Globalization;
using System.Text.Json.Nodes;
using _42.Platform.Cli.AutomationTests.Editing;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Platform.Cli.AutomationTests.Tests.Configurations;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Templates;

// Templates: sform story template, the configuration template of an annotation type in a view. A template belongs to
// the view, so the cases that change one work in a disposable project (mode P), and the Reader's read uses a generated
// view of the project, where no other test has a template.
[Area(Area.Templates)]
public sealed class TemplateTests : CliTest
{
    // TPL-TPL-01: story template (and templates) returns the template of a type.
    // An Editor sets a template of unt (probe=<v>) in a generated view of the project; as a Reader, story template unt and
    // story templates unt (the alias) in that view return it with the content.
    [Test]
    [TestCase(417)]
    [UserRole(Role.Reader)]
    [Covers("story template", "arg0")]
    [Covers("story templates", "arg0")]
    public async Task Template_Get_Returns()
    {
        var editor = await ArrangeAsEditorAsync();
        var view = CliData.ViewName();
        var probe = CliData.Value();
        (await editor.Sform(ConfigOutput.In(ProjectKey, view, "story", "template", "set", "unt", "-x", $"probe={probe}"))).ShouldSucceed();

        foreach (var command in new[] { "template", "templates" })
        {
            var template = (await Sform(ConfigOutput.In(ProjectKey, view, "story", command, "unt"))).ShouldSucceed().JsonNode();
            await Assert.That(template["Content"]?["probe"]?.GetValue<string>()).IsEqualTo(probe);
            await Assert.That(template["Version"]?.GetValue<long>() ?? 0).IsGreaterThanOrEqualTo(1L);
        }
    }

    // TPL-TPL-02: template set -x merges properties, and --replace replaces the content.
    // In the disposable project, template set unt -x a=1 stores {a}, template set unt --properties b=2 merges b into it
    // ({a, b}), and template set unt -x c=3 --replace replaces it with {c}; template unt returns {c}.
    [Test]
    [TestCase(418)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story template set", "arg0", "-x", "--properties", "--replace")]
    public async Task Template_Set_MergeOrReplace()
    {
        await CreateDisposableProjectAsync();

        var first = (await Sform(InProject("story", "template", "set", "unt", "-x", "a=1"))).ShouldSucceed().JsonNode()["Content"]!.AsObject();
        await Assert.That(Names(first)).IsEqualTo("a");

        var merged = (await Sform(InProject("story", "template", "set", "unt", "--properties", "b=2"))).ShouldSucceed().JsonNode()["Content"]!.AsObject();
        await Assert.That(Names(merged)).IsEqualTo("a, b");
        await Assert.That(merged["a"]?.GetValue<long>()).IsEqualTo(1L);
        await Assert.That(merged["b"]?.GetValue<long>()).IsEqualTo(2L);

        var replaced = (await Sform(InProject("story", "template", "set", "unt", "-x", "c=3", "--replace"))).ShouldSucceed().JsonNode()["Content"]!.AsObject();
        await Assert.That(Names(replaced)).IsEqualTo("c");

        var read = (await Sform(InProject("story", "template", "unt"))).ShouldSucceed().JsonNode()["Content"]!.AsObject();
        await Assert.That(Names(read)).IsEqualTo("c");
        await Assert.That(read["c"]?.GetValue<long>()).IsEqualTo(3L);
    }

    // TPL-TPL-03: template create -i imports a template from a file.
    // In the disposable project, template create sbt -i t.json stores the file's content ({x}), and template create sbt
    // --import t2.json the second file's ({x, y}; create merges like set, and t2.json holds x too).
    [Test]
    [TestCase(419)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story template create", "-i", "--import")]
    public async Task Template_Import_FromFile()
    {
        await CreateDisposableProjectAsync();
        var value = CliData.Value();
        await WriteFileAsync("t.json", """{ "x": 1 }""");
        await WriteFileAsync("t2.json", $$"""{ "x": 1, "y": "{{value}}" }""");

        var first = (await Sform(InProject("story", "template", "create", "sbt", "-i", "t.json"))).ShouldSucceed().JsonNode()["Content"]!.AsObject();
        await Assert.That(Names(first)).IsEqualTo("x");
        await Assert.That(first["x"]?.GetValue<long>()).IsEqualTo(1L);

        (await Sform(InProject("story", "template", "create", "sbt", "--import", "t2.json"))).ShouldSucceed();
        var read = (await Sform(InProject("story", "template", "sbt"))).ShouldSucceed().JsonNode()["Content"]!.AsObject();
        await Assert.That(Names(read)).IsEqualTo("x, y");
        await Assert.That(read["y"]?.GetValue<string>()).IsEqualTo(value);
    }

    // TPL-TPL-04: template -e saves it, and --version returns an old version.
    // In the disposable project, with three versions of the unt template (v=1, v=2, v=3): template unt -e t.json and
    // template unt --export t2.json print the template and write it (the current version, v=3) into the files; template
    // unt --version <first> returns the first version with v=1.
    [Test]
    [TestCase(420)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story template", "-e", "--export", "--version")]
    public async Task Template_ExportAndVersion()
    {
        await CreateDisposableProjectAsync();
        var written = await WriteThreeVersionsAsync("unt");

        foreach (var (option, file) in new[] { ("-e", "t.json"), ("--export", "t2.json") })
        {
            var printed = (await Sform(InProject("story", "template", "unt", option, file))).ShouldSucceed().JsonNode();
            await Assert.That(printed["Content"]?["v"]?.GetValue<long>()).IsEqualTo(3L);
            var saved = JsonNode.Parse(await ReadFileAsync(file))!;
            await Assert.That(saved["Content"]?["v"]?.GetValue<long>()).IsEqualTo(3L);
            await Assert.That(saved["Version"]?.GetValue<long>()).IsEqualTo(written[2]);
        }

        var old = (await Sform(InProject("story", "template", "unt", "--version", Text(written[0])))).ShouldSucceed().JsonNode();
        await Assert.That(old["Version"]?.GetValue<long>()).IsEqualTo(written[0]);
        await Assert.That(old["Content"]?["v"]?.GetValue<long>()).IsEqualTo(1L);
    }

    // TPL-TPL-05: template versions and template diff show the history.
    // In the disposable project, with three versions of the unt template (v=1, v=2, v=3): template versions unt prints
    // "Versions of the template for 'unt'" and the three versions, the newest first; template diff unt shows 3 against 2
    // (a deleted "v": 2, an added "v": 3), and template diff unt 3 1 shows 3 against 1.
    [Test]
    [TestCase(421)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story template versions", "arg0")]
    [Covers("story template diff", "arg0", "arg1", "arg2")]
    public async Task Template_VersionsAndDiff()
    {
        await CreateDisposableProjectAsync();
        var written = await WriteThreeVersionsAsync("unt");

        var versions = (await Sform(InProject("story", "template", "versions", "unt")))
            .ShouldSucceed()
            .ShouldContain("Versions of the template for 'unt'")
            .ShouldMatch(ConfigOutput.DatePattern());
        await Assert.That(string.Join(", ", ConfigOutput.Versions(versions))).IsEqualTo(string.Join(", ", written.Reverse()));

        var latest = (await Sform(InProject("story", "template", "diff", "unt"))).ShouldSucceed();
        await Assert.That(ConfigOutput.HasDiffLine(latest, '-', "\"v\": 2")).IsTrue().Because(latest.PlainStdOut);
        await Assert.That(ConfigOutput.HasDiffLine(latest, '+', "\"v\": 3")).IsTrue().Because(latest.PlainStdOut);
        await Assert.That(latest.PlainStdOut).DoesNotContain("\"v\": 1");

        var custom = (await Sform(InProject("story", "template", "diff", "unt", Text(written[2]), Text(written[0])))).ShouldSucceed();
        await Assert.That(ConfigOutput.HasDiffLine(custom, '-', "\"v\": 1")).IsTrue().Because(custom.PlainStdOut);
        await Assert.That(ConfigOutput.HasDiffLine(custom, '+', "\"v\": 3")).IsTrue().Because(custom.PlainStdOut);
        await Assert.That(custom.PlainStdOut).DoesNotContain("\"v\": 2");
    }

    // TPL-TPL-06: template edit -y stores the editor's change.
    // In the disposable project, with a template of unt ({base}), the fake editor adds x: template edit unt -y gives it
    // the stored template and says "Template for 'unt' has been saved (version N)." with a new version; then it adds y:
    // template edit unt --yes saves the next version. template unt then has base, x and y.
    [Test]
    [TestCase(422)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story template edit", "arg0", "-y", "--yes")]
    public async Task Template_Edit_StoresChange()
    {
        await CreateDisposableProjectAsync();
        var before = (await Sform(InProject("story", "template", "set", "unt", "-x", "base=1"))).ShouldSucceed().JsonNode()["Version"]!.GetValue<long>();
        var editor = await UseEditorAsync();

        var addX = editor.With(EditorChange.Merge("""{ "x": 1 }"""));
        var first = (await Sform(addX, InProject("story", "template", "edit", "unt", "-y")))
            .ShouldSucceed()
            .ShouldContain("Template for 'unt' has been saved (version ");
        var withX = ConfigOutput.SavedVersion(first);
        await Assert.That(withX).IsGreaterThan(before);
        await Assert.That(editor.ReadLog(addX).Single().OriginalJson()!["base"]!.GetValue<long>()).IsEqualTo(1L);

        var addY = editor.With(EditorChange.Merge("""{ "y": 2 }"""));
        var second = (await Sform(addY, InProject("story", "template", "edit", "unt", "--yes"))).ShouldSucceed();
        var withY = ConfigOutput.SavedVersion(second);
        await Assert.That(withY).IsGreaterThan(withX);
        await Assert.That(editor.ReadLog(addY).Single().OriginalJson()!["x"]!.GetValue<long>()).IsEqualTo(1L);

        var read = (await Sform(InProject("story", "template", "unt"))).ShouldSucceed().JsonNode();
        await Assert.That(read["Version"]?.GetValue<long>()).IsEqualTo(withY);
        await Assert.That(Names(read["Content"]!.AsObject())).IsEqualTo("base, x, y");
    }

    // TPL-TPL-07: template delete (and remove) deletes it, and the history stays.
    // In the disposable project, with templates of unt and sbt: template delete unt and template remove sbt say "The
    // template for '<type>' has been deleted."; template unt then finds no template (2, "The template for 'unt' has not
    // been found."), and template versions unt still lists its version.
    [Test]
    [TestCase(423)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story template delete", "arg0")]
    [Covers("story template remove", "arg0")]
    public async Task Template_Delete_KeepsHistory()
    {
        await CreateDisposableProjectAsync();
        var version = (await Sform(InProject("story", "template", "set", "unt", "-x", "a=1"))).ShouldSucceed().JsonNode()["Version"]!.GetValue<long>();
        (await Sform(InProject("story", "template", "set", "sbt", "-x", "b=1"))).ShouldSucceed();

        (await Sform(InProject("story", "template", "delete", "unt"))).ShouldSucceed().ShouldContain("The template for 'unt' has been deleted.");
        (await Sform(InProject("story", "template", "remove", "sbt"))).ShouldSucceed().ShouldContain("The template for 'sbt' has been deleted.");

        (await Sform(InProject("story", "template", "unt")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("The template for 'unt' has not been found.");
        (await Sform(InProject("story", "template", "sbt")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("The template for 'sbt' has not been found.");

        var history = (await Sform(InProject("story", "template", "versions", "unt"))).ShouldSucceed();
        await Assert.That(ConfigOutput.Versions(history)).Contains(version);
    }

    // TPL-TPL-08: An unknown type code fails with exit 2; a Reader's set is refused.
    // As a Reader of the project, template xyz ends with 2, "Please specify a valid annotation type code." and "Known
    // codes: …" before any call; template set unt -x a=1 ends with 44, the server's message ("No Contributor access to
    // '<project>'.") and "Error code: AccessDenied".
    [Test]
    [TestCase(424)]
    [UserRole(Role.Reader)]
    [Covers("story template", "arg0")]
    [Covers("story template set")]
    public async Task Template_InvalidTypeOrReader_Fails()
    {
        (await Sform(InProject("story", "template", "xyz")))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain("Please specify a valid annotation type code.")
            .ShouldContain("Known codes: ");

        (await Sform(InProject("story", "template", "set", "unt", "-x", "a=1")))
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldContain($"No Contributor access to '{ProjectKey}'.")
            .ShouldContain("Error code: AccessDenied");
    }

    // TPL-TPL-09: Every template command takes its project from --projectKey and its view from -v or --view.
    // The Owner creates the disposable project <B> and sets the target project <A> as the default (account set -p <A>).
    // Each template command runs once with --projectKey <B> --view <view> on unt and once with --projectKey <B> -v
    // <view> on sbt (<view> a generated view): template set (a=1), template (reads a=1), template edit -y (the fake
    // editor adds b), template versions (two versions), template diff (b added), template delete. Before the deletes
    // neither type has a template in <view> of <A> (the default project) or in the view default of <B>; after them none
    // in <view> of <B> either.
    [Test]
    [TestCase(425)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story template set", "--projectKey", "-v", "--view")]
    [Covers("story template", "--projectKey", "-v", "--view")]
    [Covers("story template edit", "--projectKey", "-v", "--view")]
    [Covers("story template versions", "--projectKey", "-v", "--view")]
    [Covers("story template diff", "--projectKey", "-v", "--view")]
    [Covers("story template delete", "--projectKey", "-v", "--view")]
    public async Task TemplateCommands_ContextOptions_TargetProjectAndView()
    {
        var b = await CreateDisposableProjectAsync(actInIt: false);
        var view = CliData.ViewName();
        (await Sform("account", "set", "-p", Target.ProjectKey)).ShouldSucceed();
        var editor = await UseEditorAsync();
        var runs = new (string Type, string ViewOption)[] { ("unt", "--view"), ("sbt", "-v") };

        foreach (var (type, viewOption) in runs)
        {
            var set = (await Sform(In("story", "template", "set", type, "-x", "a=1"))).ShouldSucceed().JsonNode();
            await Assert.That(set["Content"]?["a"]?.GetValue<long>()).IsEqualTo(1L);

            var read = (await Sform(In("story", "template", type))).ShouldSucceed().JsonNode();
            await Assert.That(read["Content"]?["a"]?.GetValue<long>()).IsEqualTo(1L);

            var added = editor.With(EditorChange.Merge("""{ "b": 2 }"""));
            (await Sform(added, In("story", "template", "edit", type, "-y"))).ShouldSucceed().ShouldContain($"Template for '{type}' has been saved (version ");
            await Assert.That(editor.ReadLog(added).Single().OriginalJson()!["a"]!.GetValue<long>()).IsEqualTo(1L);

            var versions = (await Sform(In("story", "template", "versions", type))).ShouldSucceed();
            await Assert.That(ConfigOutput.Versions(versions).Count).IsEqualTo(2);

            var diff = (await Sform(In("story", "template", "diff", type))).ShouldSucceed();
            await Assert.That(ConfigOutput.HasDiffLine(diff, '+', "\"b\": 2")).IsTrue().Because(diff.PlainStdOut);

            // Nothing went to <view> of the default project <A>, or to the view default of <B>.
            (await Sform("story", "template", type, "-v", view))
                .ShouldExitWith(CliExit.WrongInput)
                .ShouldContain($"The template for '{type}' has not been found.");
            (await Sform("story", "template", type, "-p", b))
                .ShouldExitWith(CliExit.WrongInput)
                .ShouldContain($"The template for '{type}' has not been found.");

            (await Sform(In("story", "template", "delete", type)))
                .ShouldSucceed()
                .ShouldContain($"The template for '{type}' has been deleted.");
            (await Sform(ConfigOutput.In(b, view, "story", "template", type)))
                .ShouldExitWith(CliExit.WrongInput)
                .ShouldContain($"The template for '{type}' has not been found.");

            string[] In(params string[] arguments) => [.. arguments, "--projectKey", b, viewOption, view];
        }
    }

    private static string Text(long version) => version.ToString(CultureInfo.InvariantCulture);

    // The property names of a content, in order, e.g. "a, b".
    private static string Names(JsonObject content) => string.Join(", ", content.Select(property => property.Key));

    // Three versions of the template of the type in the acting project (v=1, v=2, v=3); their versions in order.
    private async Task<long[]> WriteThreeVersionsAsync(string type)
    {
        var versions = new long[3];
        for (var index = 0; index < versions.Length; index++)
        {
            var set = (await Sform(InProject("story", "template", "set", type, "-x", $"v={index + 1}"))).ShouldSucceed();
            versions[index] = set.JsonNode()["Version"]!.GetValue<long>();
        }

        return versions;
    }
}
