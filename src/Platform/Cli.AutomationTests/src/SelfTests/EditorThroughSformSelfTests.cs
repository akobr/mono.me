using System.Text;
using System.Text.Json.Nodes;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Editing;
using _42.Platform.Cli.AutomationTests.Running;

namespace _42.Platform.Cli.AutomationTests.SelfTests;

// Mode E end to end without a target: the built sform runs story config edit as a machine with an API key against a stub
// of the API (StubApi; the configuration does not exist yet, so sform opens the editor on {} and creates the
// configuration with a POST), with the editor section of sform.settings.json in its SFORM_HOME naming the fake editor. It shows that sform starts
// the fake editor's apphost from the custom command with its temporary file, waits for it, and reacts to each change as
// the catalogue's E cases expect.
[Category(CliCategories.SelfTest)]
public sealed class EditorThroughSformSelfTests
{
    private const string Key = "rst.selftest-editor";

    [Test]
    public async Task ConfigEdit_Merge_SavesTheEditedConfiguration()
    {
        await using var api = Api();
        using var sandbox = new SelfTestSandbox();
        var editor = await UseEditorAsync(sandbox);

        var result = await EditAsync(sandbox, api, editor, EditorChange.Merge("""{ "a": 2, "b": { "c": true } }"""));

        result.ShouldSucceed().ShouldContain($"Configuration for '{Key}' has been saved (version 1).");
        var opened = editor.ReadLog().Single();
        await Assert.That(opened.FileName!).StartsWith($"config-{Key}-");
        await Assert.That(opened.OriginalJson()!.ToJsonString()).IsEqualTo("{}");
        var saved = api.Requests.SingleOrDefault(request => request.Method == "POST");
        await Assert.That(saved).IsNotNull().Because(api.Describe());
        var sent = JsonNode.Parse(saved!.Body)!;
        await Assert.That(sent["a"]!.GetValue<int>()).IsEqualTo(2);
        await Assert.That(sent["b"]!["c"]!.GetValue<bool>()).IsTrue();
        await Assert.That(saved.Headers["Authorization"]).StartsWith("ApiKey 2s.");
    }

    [Test]
    public async Task ConfigEdit_None_ChangesNothing()
    {
        await using var api = Api();
        using var sandbox = new SelfTestSandbox();
        var editor = await UseEditorAsync(sandbox);

        var result = await EditAsync(sandbox, api, editor, EditorChange.None);

        result.ShouldExitWith(CliExit.NoWorkNeeded).ShouldContain("No changes detected.");
        await Assert.That(editor.ReadLog().Single().Step).IsEqualTo("none");
        await Assert.That(api.Requests.Any(request => request.Method != "GET")).IsFalse().Because(api.Describe());
    }

    [Test]
    public async Task ConfigEdit_Invalid_SavesNothing()
    {
        await using var api = Api();
        using var sandbox = new SelfTestSandbox();
        var editor = await UseEditorAsync(sandbox);

        var result = await EditAsync(sandbox, api, editor, EditorChange.Invalid);

        result.ShouldExitWith(CliExit.WrongInput).ShouldContain("Invalid JSON").ShouldContain("Nothing was saved.");
        await Assert.That(editor.ReadLog().Count).IsEqualTo(1);
        await Assert.That(api.Requests.Any(request => request.Method != "GET")).IsFalse().Because(api.Describe());
    }

    [Test]
    public async Task ConfigEdit_EditorFails_Crash()
    {
        await using var api = Api();
        using var sandbox = new SelfTestSandbox();
        var editor = await UseEditorAsync(sandbox);

        var result = await EditAsync(sandbox, api, editor, EditorChange.Exit(3));

        result.ShouldExitWith(CliExit.Crash).ShouldContain("Editor exited with code 3.");
        await Assert.That(editor.ReadLog().Single().ExitCode).IsEqualTo(3);
    }

    // The configuration does not exist: no versions, and the POST that creates it (SetConfigurationAsync) answers with the
    // saved model.
    private static StubApi Api()
    {
        return new StubApi(request =>
        {
            var path = request.Path.Split('?')[0];
            if (request.Method == "GET" && path.EndsWith($"/configuration/{Key}/versions", StringComparison.Ordinal))
            {
                return StubResponse.Json("[]");
            }

            if (request.Method == "POST" && path.EndsWith($"/configuration/{Key}", StringComparison.Ordinal))
            {
                var content = string.IsNullOrWhiteSpace(request.Body) ? "{}" : request.Body;
                return StubResponse.Json($$"""{ "AnnotationKey": "{{Key}}", "Version": 1, "Content": {{content}} }""");
            }

            return StubResponse.NotFound;
        });
    }

    private static async Task<FakeEditor> UseEditorAsync(SelfTestSandbox sandbox)
    {
        var editor = new FakeEditor(sandbox.WorkingDirectory, sandbox.FileSystem);
        sandbox.Home.WriteSettingsSection("editor", new JsonObject { ["editorType"] = "Custom", ["customCommand"] = editor.Command });
        await Task.CompletedTask;
        return editor;
    }

    private static Task<SformResult> EditAsync(SelfTestSandbox sandbox, StubApi api, FakeEditor editor, EditorChange change)
    {
        var segment = Convert.ToBase64String(Encoding.UTF8.GetBytes("selftest:editor:m-selftest")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var machine = MachineCredentials.ForApiKey($"2s.{segment}.not-a-secret-of-any-target", api.Url);
        return sandbox.Runner.RunAsync(["story", "config", "edit", Key, "-y"], editor.With(change, new SformRunOptions { Machine = machine, Home = sandbox.Home }));
    }
}
