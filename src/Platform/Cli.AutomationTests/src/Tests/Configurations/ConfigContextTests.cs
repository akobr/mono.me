using _42.Platform.Cli.AutomationTests.Editing;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Configurations;

// Context: every configuration command takes its project from --projectKey and its view from -v or --view.
[Area(Area.Configurations)]
public sealed class ConfigContextTests : CliTest
{
    // CFG-CTX-01: Every configuration command takes its project from --projectKey and its view from -v or --view.
    // The Owner creates the disposable project <B>, sets the target project <A> as the default (account set -p <A>), and
    // creates <rst> and <rst2> in a generated view <view> of <B>. Then each configuration command runs once with
    // --projectKey <B> --view <view> on <rst> and once with --projectKey <B> -v <view> on <rst2>: config set (a=1),
    // config (reads a=1), config edit -y (the fake editor adds b), config patch -i p.json -y (adds c), config versions
    // (three versions), config diff (c added), config composition, config delete. Before the deletes neither key has a
    // configuration in the default project <A> (its view default or <view>) or in the view default of <B>; after them
    // the stored content in <view> of <B> is gone ({}).
    [Test]
    [TestCase(441)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("story config set", "--projectKey", "-v", "--view")]
    [Covers("story config", "--projectKey", "-v", "--view")]
    [Covers("story config edit", "--projectKey", "-v", "--view")]
    [Covers("story config patch", "--projectKey", "-v", "--view")]
    [Covers("story config versions", "--projectKey", "-v", "--view")]
    [Covers("story config diff", "--projectKey", "-v", "--view")]
    [Covers("story config composition", "--projectKey", "-v", "--view")]
    [Covers("story config delete", "--projectKey", "-v", "--view")]
    public async Task ConfigCommands_ContextOptions_TargetProjectAndView()
    {
        var b = await CreateDisposableProjectAsync(actInIt: false);
        var view = CliData.ViewName();
        var key = CliData.ResponsibilityKey();
        var key2 = CliData.ResponsibilityKey();
        (await Sform("account", "set", "-p", Target.ProjectKey)).ShouldSucceed();
        (await Sform(ConfigOutput.In(b, view, "story", "set", key))).ShouldSucceed();
        (await Sform(ConfigOutput.In(b, view, "story", "set", key2))).ShouldSucceed();
        await WriteFileAsync("p.json", """[ { "op": "add", "path": "/c", "value": 3 } ]""");
        var editor = await UseEditorAsync();
        var runs = new (string Key, string ViewOption)[] { (key, "--view"), (key2, "-v") };

        foreach (var (annotation, viewOption) in runs)
        {
            var set = (await Sform(In("story", "config", "set", annotation, "-x", "a=1"))).ShouldSucceed().JsonNode();
            await Assert.That(set["Version"]?.GetValue<long>() ?? 0).IsGreaterThanOrEqualTo(1L);

            var read = (await Sform(In("story", "config", annotation))).ShouldSucceed().JsonNode();
            await Assert.That(read["AnnotationKey"]?.GetValue<string>()).IsEqualTo(annotation);
            await Assert.That(read["Content"]?["a"]?.GetValue<long>()).IsEqualTo(1L);

            var added = editor.With(EditorChange.Merge("""{ "b": 2 }"""));
            (await Sform(added, In("story", "config", "edit", annotation, "-y"))).ShouldSucceed().ShouldContain($"Configuration for '{annotation}' has been saved (version ");
            await Assert.That(editor.ReadLog(added).Single().OriginalJson()!["a"]!.GetValue<long>()).IsEqualTo(1L);

            var patched = (await Sform(In("story", "config", "patch", annotation, "-i", "p.json", "-y"))).ShouldSucceed();
            var content = ConfigOutput.LastJson(patched)["Content"];
            await Assert.That(content?["b"]?.GetValue<long>()).IsEqualTo(2L);
            await Assert.That(content?["c"]?.GetValue<long>()).IsEqualTo(3L);

            var versions = (await Sform(In("story", "config", "versions", annotation))).ShouldSucceed();
            await Assert.That(ConfigOutput.Versions(versions).Count).IsEqualTo(3);

            var diff = (await Sform(In("story", "config", "diff", annotation))).ShouldSucceed();
            await Assert.That(ConfigOutput.HasDiffLine(diff, '+', "\"c\": 3")).IsTrue().Because(diff.PlainStdOut);

            (await Sform(In("story", "config", "composition", annotation)))
                .ShouldSucceed()
                .ShouldContain($"Composition of the configuration for '{annotation}'");

            // Nothing went to the default project <A> (its view default or <view>), or to the view default of <B>.
            (await Sform("story", "config", annotation))
                .ShouldExitWith(CliExit.WrongInput)
                .ShouldContain($"The configuration for '{annotation}' has not been found.");
            (await Sform("story", "config", annotation, "-v", view))
                .ShouldExitWith(CliExit.WrongInput)
                .ShouldContain($"The configuration for '{annotation}' has not been found.");
            (await Sform("story", "config", annotation, "-p", b))
                .ShouldExitWith(CliExit.WrongInput)
                .ShouldContain($"The configuration for '{annotation}' has not been found.");

            (await Sform(In("story", "config", "delete", annotation)))
                .ShouldSucceed()
                .ShouldContain($"The configuration for '{annotation}' has been deleted.");
            var deleted = (await Sform(ConfigOutput.In(b, view, "story", "config", annotation, "--content-only"))).ShouldSucceed().JsonNode();
            await Assert.That(deleted.AsObject().Count).IsEqualTo(0);

            string[] In(params string[] arguments) => [.. arguments, "--projectKey", b, viewOption, view];
        }
    }
}
