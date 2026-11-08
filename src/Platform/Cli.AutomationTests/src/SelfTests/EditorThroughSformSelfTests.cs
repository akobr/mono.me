using System.Text;
using System.Text.Json.Nodes;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Editing;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Platform.Cli.AutomationTests.Running.Processes;

namespace _42.Platform.Cli.AutomationTests.SelfTests;

// Mode E end to end without a target: the built sform runs story config edit as a machine with an API key against a stub
// of the API (StubApi; the configuration does not exist yet, so sform opens the editor on {} and creates the
// configuration with a POST), with the editor section of sform.settings.json in its SFORM_HOME naming the fake editor. It shows that sform starts
// the fake editor's apphost from the custom command with its temporary file, waits for it, and reacts to each change as
// the catalogue's E cases expect. story edit, which needs an interactive console, runs the same way in a pseudo terminal
// (modes T and E together, as the Edit cases of the Annotations run it), and ends with 43 before any request without one.
[Category(CliCategories.SelfTest)]
public sealed class EditorThroughSformSelfTests
{
    private const string Key = "rst.selftest-editor";
    private const string AnnotationKey = "rst.selftest-annotation";

    // The stored annotation of story edit, as a read answers it. The stub sends no ETag, so sform reads it again before the
    // write instead of sending If-Match.
    private const string StoredAnnotation = """
        {
          "AnnotationType": "Responsibility", "ProjectName": "editor", "ViewName": "default",
          "AnnotationKey": "rst.selftest-annotation", "Name": "selftest-annotation",
          "Title": "Stored", "Labels": ["old"], "Values": { "keep": 1, "drop": 2 }
        }
        """;

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

    // story edit in a pseudo terminal with the fake editor: sform opens the editor on the editable document of the stored
    // annotation, shows the changes, asks "Do you want to save these changes" and, after Yes, writes the edited document
    // whole with a POST (a replace: the dropped value and the old label are not sent).
    [Test]
    [Category(CliCategories.Terminal)]
    public async Task StoryEdit_InATerminal_SavesTheEditedDocument()
    {
        SkipWithoutTerminal();
        await using var api = AnnotationApi();
        using var sandbox = new SelfTestSandbox();
        var editor = await UseEditorAsync(sandbox);
        var options = editor.With(
            EditorChange.Merge("""{ "Title": "Edited", "Labels": ["a", "b"], "Values": { "drop": null, "added": "x" } }"""),
            new SformRunOptions { Machine = MachineOf(api), Home = sandbox.Home, Terminal = true });

        await using var terminal = await sandbox.Runner.StartAsync(["story", "edit", AnnotationKey], options);
        await terminal.ExpectAsync("Do you want to save these changes");
        await terminal.SendLineAsync("y");
        await terminal.ExpectAsync($"Annotation '{AnnotationKey}' has been saved.");
        var result = await terminal.WaitForExitAsync();

        result.ShouldSucceed();
        await Assert.That(result.IsTerminal).IsTrue();
        var opened = editor.ReadLog(options).Single();
        await Assert.That(opened.FileName!).StartsWith($"annotation-{AnnotationKey}-");
        await Assert.That(opened.OriginalJson()!["Title"]!.GetValue<string>()).IsEqualTo("Stored");
        await Assert.That(opened.OriginalJson()!["Values"]!["drop"]!.GetValue<int>()).IsEqualTo(2);
        var saved = api.Requests.SingleOrDefault(request => request.Method == "POST");
        await Assert.That(saved).IsNotNull().Because(api.Describe());
        var sent = JsonNode.Parse(saved!.Body)!;
        await Assert.That(sent["Title"]!.GetValue<string>()).IsEqualTo("Edited");
        await Assert.That(sent["Labels"]!.AsArray().Select(label => label!.GetValue<string>())).IsEquivalentTo(new[] { "a", "b" });
        await Assert.That(sent["Values"]!["keep"]!.GetValue<int>()).IsEqualTo(1);
        await Assert.That(sent["Values"]!["added"]!.GetValue<string>()).IsEqualTo("x");
        await Assert.That(sent["Values"]!.AsObject().ContainsKey("drop")).IsFalse();
    }

    // story edit with the input and output redirected: 43 and the commands that edit without a terminal, before any
    // request and without starting the editor.
    [Test]
    public async Task StoryEdit_Redirected_NeedsATerminalBeforeAnyRequest()
    {
        await using var api = AnnotationApi();
        using var sandbox = new SelfTestSandbox();
        var editor = await UseEditorAsync(sandbox);
        var options = editor.With(EditorChange.Merge("""{ "Title": "Edited" }"""), new SformRunOptions { Machine = MachineOf(api), Home = sandbox.Home });

        var result = await sandbox.Runner.RunAsync(["story", "edit", AnnotationKey], options);

        result.ShouldExitWith(CliExit.InteractionNeeded)
            .ShouldContain($"sform story edit needs an interactive console, without redirected input or output. Without one: sform story get {AnnotationKey} -e <file>, edit the file, then sform story set {AnnotationKey} -i <file> --replace.");
        await Assert.That(api.Requests.IsEmpty).IsTrue().Because(api.Describe());
        await Assert.That(editor.ReadLog(options).Count).IsEqualTo(0);
    }

    private static void SkipWithoutTerminal()
    {
        if (TerminalSupport.Reason() is { } reason)
        {
            Skip.Test($"Terminal: {reason}");
        }
    }

    // The stored annotation of story edit (both reads), and the POST of its write, answered with what was sent.
    private static StubApi AnnotationApi()
    {
        return new StubApi(request =>
        {
            var path = request.Path.Split('?')[0];
            if (!path.EndsWith($"/annotations/{AnnotationKey}", StringComparison.Ordinal))
            {
                return StubResponse.NotFound;
            }

            return request.Method switch
            {
                "GET" => StubResponse.Json(StoredAnnotation),
                "POST" => StubResponse.Json(request.Body),
                _ => StubResponse.NotFound,
            };
        });
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
        return sandbox.Runner.RunAsync(["story", "config", "edit", Key, "-y"], editor.With(change, new SformRunOptions { Machine = MachineOf(api), Home = sandbox.Home }));
    }

    // An API key machine of the organization selftest and its project editor, against the stub.
    private static MachineCredentials MachineOf(StubApi api)
    {
        var segment = Convert.ToBase64String(Encoding.UTF8.GetBytes("selftest:editor:m-selftest")).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return MachineCredentials.ForApiKey($"2s.{segment}.not-a-secret-of-any-target", api.Url);
    }
}
