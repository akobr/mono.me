using System.Text.Json.Nodes;
using _42.Platform.Cli.AutomationTests.Editing;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Annotations;

// Context: every annotation command takes the project from --projectKey and the view from -v or --view (story edit, which
// needs a terminal and the editor, in a case of its own).
[Area(Area.Annotations)]
public sealed class AnnotationContextTests : CliTest
{
    // ANN-CTX-01: Every annotation command takes its project from --projectKey and its view from -v or --view.
    // The Owner creates the disposable project <B> and sets the target project <A> as the default (account set -p <A>).
    // With --projectKey <B> and --view <view> (one run) or -v <view> (the other), story set creates <rst1> and <rst2> in
    // <view> of <B>; story lists both there; story get and story history read them; story export -o b1.zip and b2.zip hold
    // them; story delete deletes them and story deleted lists them there; story restore brings them back; story import
    // b1.zip into <view2> and b2.zip into <view3> of <B> creates them there. Nothing reaches <A> (story get without -p
    // reads the default project and finds neither) or the view default of <B> (no annotation, nothing deleted).
    [Test]
    [TestCase(367)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story set", "--projectKey", "-v", "--view")]
    [Covers("story", "--projectKey", "-v", "--view")]
    [Covers("story get", "--projectKey", "-v", "--view")]
    [Covers("story history", "--projectKey", "-v", "--view")]
    [Covers("story export", "--projectKey", "-v", "--view")]
    [Covers("story delete", "--projectKey", "-v", "--view")]
    [Covers("story deleted", "--projectKey", "-v", "--view")]
    [Covers("story restore", "--projectKey", "-v", "--view")]
    [Covers("story import", "--projectKey", "-v", "--view")]
    public async Task AnnotationCommands_ContextOptions_TargetProjectAndView()
    {
        var projectB = await CreateDisposableProjectAsync(actInIt: false);
        var nameB = projectB[(projectB.IndexOf('.', StringComparison.Ordinal) + 1)..];
        (await Sform("account", "set", "-p", Target.ProjectKey, "-v", Target.View)).ShouldSucceed();
        var view = CliData.ViewName();
        var view2 = CliData.ViewName();
        var view3 = CliData.ViewName();
        var first = CliData.ResponsibilityKey();
        var second = CliData.ResponsibilityKey();
        string[] WithView(params string[] arguments) => [.. arguments, "--projectKey", projectB, "--view", view];
        string[] WithV(params string[] arguments) => [.. arguments, "--projectKey", projectB, "-v", view];

        foreach (var (key, arguments) in new[] { (first, WithView("story", "set", first)), (second, WithV("story", "set", second)) })
        {
            var set = (await Sform(arguments)).ShouldSucceed().JsonNode();
            await Assert.That(set["AnnotationKey"]!.GetValue<string>()).IsEqualTo(key);
            await Assert.That(set["ProjectName"]!.GetValue<string>()).IsEqualTo(nameB);
            await Assert.That(set["ViewName"]!.GetValue<string>()).IsEqualTo(view);
        }

        foreach (var arguments in new[] { WithView("story"), WithV("story") })
        {
            await Assert.That(AnnotationOutput.Keys((await Sform(arguments)).ShouldSucceed())).IsEquivalentTo(new[] { first, second });
        }

        foreach (var (key, arguments) in new[] { (first, WithView("story", "get", first)), (second, WithV("story", "get", second)) })
        {
            var annotation = (await Sform(arguments)).ShouldSucceed().JsonNode();
            await Assert.That(annotation["AnnotationKey"]!.GetValue<string>()).IsEqualTo(key);
            await Assert.That(annotation["ViewName"]!.GetValue<string>()).IsEqualTo(view);
        }

        (await Sform(WithView("story", "history", first))).ShouldSucceed().ShouldHaveLine($"# History of {first}").ShouldContain("Created");
        (await Sform(WithV("story", "history", second))).ShouldSucceed().ShouldHaveLine($"# History of {second}").ShouldContain("Created");

        (await Sform(WithView("story", "export", "-o", "b1.zip"))).ShouldSucceed().ShouldContain($"of {projectB}.{view} (folder)");
        (await Sform(WithV("story", "export", "-o", "b2.zip"))).ShouldSucceed().ShouldContain($"of {projectB}.{view} (folder)");
        foreach (var zip in new[] { "b1.zip", "b2.zip" })
        {
            var entries = ViewArchive.Entries(FileSystem, FileSystem.Path.Combine(WorkingDirectory, zip));
            await Assert.That(entries).Contains($"annotations/{first}.json");
            await Assert.That(entries).Contains($"annotations/{second}.json");
        }

        // Nothing in the default project <A> (the commands without -p read it) or in the view default of <B>.
        foreach (var key in new[] { first, second })
        {
            (await Sform("story", "get", key))
                .ShouldExitWith(CliExit.WrongInput)
                .ShouldContain($"The annotation '{key}' has not been found.");
        }

        (await Sform("story", "-p", projectB, "-v", "default")).ShouldSucceed().ShouldContain("No annotation exists yet.");

        (await Sform(WithView("story", "delete", first))).ShouldSucceed().ShouldContain($"Deleted '{first}'.");
        (await Sform(WithV("story", "delete", second))).ShouldSucceed().ShouldContain($"Deleted '{second}'.");
        foreach (var arguments in new[] { WithView("story", "deleted"), WithV("story", "deleted") })
        {
            await Assert.That(AnnotationOutput.TableKeys((await Sform(arguments)).ShouldSucceed())).IsEquivalentTo(new[] { first, second });
        }

        (await Sform("story", "deleted", "-p", projectB, "-v", "default")).ShouldSucceed().ShouldContain("No annotation is deleted.");

        foreach (var (key, arguments) in new[] { (first, WithView("story", "restore", first)), (second, WithV("story", "restore", second)) })
        {
            var restored = (await Sform(arguments)).ShouldSucceed();
            await Assert.That(AnnotationOutput.Section(restored, "Restored")).IsEquivalentTo(new[] { key });
            (await Sform("story", "get", key, "-p", projectB, "-v", view)).ShouldSucceed();
        }

        (await Sform("story", "import", "b1.zip", "--projectKey", projectB, "--view", view2))
            .ShouldSucceed()
            .ShouldContain($"Importing b1.zip into {projectB}.{view2}");
        (await Sform("story", "import", "b2.zip", "--projectKey", projectB, "-v", view3))
            .ShouldSucceed()
            .ShouldContain($"Importing b2.zip into {projectB}.{view3}");
        foreach (var importedView in new[] { view2, view3 })
        {
            var imported = (await Sform("story", "-p", projectB, "-v", importedView)).ShouldSucceed();
            await Assert.That(AnnotationOutput.Keys(imported)).IsEquivalentTo(new[] { first, second });
        }

        (await Sform("story", "-p", projectB, "-v", "default")).ShouldSucceed().ShouldContain("No annotation exists yet.");
    }

    // ANN-CTX-02: story edit takes its project from --projectKey and its view from -v or --view.
    // As an Editor without a default project, <rst1> is set in <view> of the project (a view other than the role's) with
    // a=<value>, and <rst2> is missing. In a terminal, story edit <rst1> --projectKey <project> --view <view> opens the fake
    // editor on the document of <view> (a=<value>); the title it sets is saved after Yes. story edit <rst2> --projectKey
    // <project> -v <view> says "does not exist yet, creating new." and saves the title it sets. story get -p <project> -v
    // <view> reads both with their titles, and the role's view has neither.
    [Test]
    [TestCase(473)]
    [UserRole(Role.Editor)]
    [Terminal]
    [Covers("story edit", "--projectKey", "-v", "--view")]
    public async Task StoryEdit_ContextOptions_TargetProjectAndView()
    {
        var view = CliData.ViewName();
        var first = CliData.ResponsibilityKey();
        var second = CliData.ResponsibilityKey();
        var value = CliData.Value();
        var titles = new Dictionary<string, string>(StringComparer.Ordinal) { [first] = CliData.Value(), [second] = CliData.Value() };
        (await Sform("story", "set", first, "-p", ProjectKey, "-v", view, "-c", $"a={value}")).ShouldSucceed();
        var editor = await UseEditorAsync();

        var withView = editor.With(EditorChange.Merge(new JsonObject { ["Title"] = titles[first] }));
        await using (var terminal = await StartSformInTerminal(withView, "story", "edit", first, "--projectKey", ProjectKey, "--view", view))
        {
            await AnnotationEditorTests.AnswerSaveAsync(terminal, save: true);
            await terminal.ExpectAsync($"Annotation '{first}' has been saved.");
            (await terminal.WaitForExitAsync()).ShouldSucceed();
        }

        await Assert.That(editor.ReadLog(withView).Single().OriginalJson()!["Values"]?["a"]?.GetValue<string>()).IsEqualTo(value);

        var withV = editor.With(EditorChange.Merge(new JsonObject { ["Title"] = titles[second] }));
        await using (var terminal = await StartSformInTerminal(withV, "story", "edit", second, "--projectKey", ProjectKey, "-v", view))
        {
            await terminal.ExpectAsync($"Annotation '{second}' does not exist yet, creating new.");
            await AnnotationEditorTests.AnswerSaveAsync(terminal, save: true);
            await terminal.ExpectAsync($"Annotation '{second}' has been saved.");
            (await terminal.WaitForExitAsync()).ShouldSucceed();
        }

        foreach (var (key, title) in titles)
        {
            var annotation = (await Sform("story", "get", key, "-p", ProjectKey, "-v", view)).ShouldSucceed().JsonNode();
            await Assert.That(annotation["Title"]?.GetValue<string>()).IsEqualTo(title);
            await Assert.That(annotation["ViewName"]?.GetValue<string>()).IsEqualTo(view);
            (await Sform(InProject("story", "get", key)))
                .ShouldExitWith(CliExit.WrongInput)
                .ShouldContain($"The annotation '{key}' has not been found.");
        }
    }
}
