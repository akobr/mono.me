using System.Diagnostics;
using System.Text.Json.Nodes;
using _42.Platform.Cli.AutomationTests.Editing;
using _42.Platform.Cli.AutomationTests.Running;

namespace _42.Platform.Cli.AutomationTests.SelfTests;

// The fake editor of mode E (src/Platform/Cli.AutomationTests/editor) started the way sform starts a custom editor
// (EditorService.OpenFileInEditorAsync: the command, the quoted path as the argument string, no shell, sform's
// environment), once per change: what it does to the file, its exit code, its log. No sform and no target.
[Category(CliCategories.SelfTest)]
public sealed class FakeEditorSelfTests
{
    private const string Document = """
        {
          "a": 1,
          "nested": { "keep": true, "drop": "x" },
          "list": [ 1, 2 ]
        }
        """;

    [Test]
    public async Task Editor_IsBuiltAndRecorded_WithoutASpace()
    {
        var command = FakeEditor.ResolveCommand();

        await Assert.That(FakeEditor.RecordedPath).IsNotNull();
        await Assert.That(FakeEditor.RecordedPath!).EndsWith("42.Platform.Cli.AutomationTests.Editor.dll");
        await Assert.That(new System.IO.Abstractions.FileSystem().File.Exists(command)).IsTrue();
        await Assert.That(command).DoesNotContain(" ");
    }

    [Test]
    public async Task Replace_WritesTheContent()
    {
        using var sandbox = new SelfTestSandbox();
        var editor = new FakeEditor(sandbox.WorkingDirectory, sandbox.FileSystem);
        var file = WriteDocument(sandbox, "config-rst.app-1a2b3c4d.json");

        var exitCode = await OpenAsync(editor, EditorChange.Replace("""{ "b": 2 }"""), file);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(sandbox.FileSystem.File.ReadAllText(file)).IsEqualTo("""{ "b": 2 }""");
        var opened = editor.ReadLog().Single();
        await Assert.That(opened.File).IsEqualTo(file);
        await Assert.That(opened.FileName).IsEqualTo("config-rst.app-1a2b3c4d.json");
        await Assert.That(opened.Step).IsEqualTo("replace");
        await Assert.That(opened.Run).IsEqualTo("run-1");
        await Assert.That(opened.OriginalJson()!["a"]!.GetValue<int>()).IsEqualTo(1);
        await Assert.That(opened.Written).IsEqualTo("""{ "b": 2 }""");
        await Assert.That(opened.Error).IsNull();
    }

    // RFC 7396: members merge, null removes, an array is replaced as a whole.
    [Test]
    public async Task Merge_AppliesTheMergePatch()
    {
        using var sandbox = new SelfTestSandbox();
        var editor = new FakeEditor(sandbox.WorkingDirectory, sandbox.FileSystem);
        var file = WriteDocument(sandbox, "template-unt-0a0b0c0d.json");

        var exitCode = await OpenAsync(editor, EditorChange.Merge("""{ "a": 2, "nested": { "drop": null, "new": "y" }, "list": [ 3 ], "c": { "d": null, "e": 1 } }"""), file);

        await Assert.That(exitCode).IsEqualTo(0);
        var merged = JsonNode.Parse(sandbox.FileSystem.File.ReadAllText(file))!;
        await Assert.That(merged["a"]!.GetValue<int>()).IsEqualTo(2);
        await Assert.That(merged["nested"]!["keep"]!.GetValue<bool>()).IsTrue();
        await Assert.That(merged["nested"]!.AsObject().ContainsKey("drop")).IsFalse();
        await Assert.That(merged["nested"]!["new"]!.GetValue<string>()).IsEqualTo("y");
        await Assert.That(merged["list"]!.AsArray().Count).IsEqualTo(1);
        await Assert.That(merged["c"]!.ToJsonString()).IsEqualTo("""{"e":1}""");
        await Assert.That(editor.ReadLog().Single().Step).IsEqualTo("merge");
    }

    [Test]
    public async Task None_LeavesTheFile()
    {
        using var sandbox = new SelfTestSandbox();
        var editor = new FakeEditor(sandbox.WorkingDirectory, sandbox.FileSystem);
        var file = WriteDocument(sandbox, "schema-x.json");

        var exitCode = await OpenAsync(editor, EditorChange.None, file);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(sandbox.FileSystem.File.ReadAllText(file)).IsEqualTo(Document);
        var opened = editor.ReadLog().Single();
        await Assert.That(opened.Step).IsEqualTo("none");
        await Assert.That(opened.Written).IsNull();
        await Assert.That(opened.Original).IsEqualTo(Document);
    }

    [Test]
    public async Task Invalid_WritesNoJson()
    {
        using var sandbox = new SelfTestSandbox();
        var editor = new FakeEditor(sandbox.WorkingDirectory, sandbox.FileSystem);
        var file = WriteDocument(sandbox, "config-y.json");

        var exitCode = await OpenAsync(editor, EditorChange.Invalid, file);

        await Assert.That(exitCode).IsEqualTo(0);
        var written = sandbox.FileSystem.File.ReadAllText(file);
        await Assert.That(() => JsonNode.Parse(written)).Throws<System.Text.Json.JsonException>();
        await Assert.That(editor.ReadLog().Single().Written).IsEqualTo(written);
    }

    [Test]
    public async Task Exit_EndsWithTheCode_AndLeavesTheFile()
    {
        using var sandbox = new SelfTestSandbox();
        var editor = new FakeEditor(sandbox.WorkingDirectory, sandbox.FileSystem);
        var file = WriteDocument(sandbox, "config-z.json");

        var exitCode = await OpenAsync(editor, EditorChange.Exit(7), file);

        await Assert.That(exitCode).IsEqualTo(7);
        await Assert.That(sandbox.FileSystem.File.ReadAllText(file)).IsEqualTo(Document);
        var opened = editor.ReadLog().Single();
        await Assert.That(opened.ExitCode).IsEqualTo(7);
        await Assert.That(opened.Step).IsEqualTo("exit:7");
    }

    // The n-th invocation of a run does the n-th step (sform opens the editor again after invalid JSON in a terminal), the
    // last step repeats, and the next run starts again at the first step.
    [Test]
    public async Task Sequence_StepsPerInvocation_CountedPerRun()
    {
        using var sandbox = new SelfTestSandbox();
        var editor = new FakeEditor(sandbox.WorkingDirectory, sandbox.FileSystem);
        var file = WriteDocument(sandbox, "config-seq.json");
        var options = editor.With(EditorChange.Sequence(EditorChange.Invalid, EditorChange.Replace("""{ "fixed": true }""")));

        await Assert.That(await OpenAsync(editor, options, file)).IsEqualTo(0);
        await Assert.That(() => JsonNode.Parse(sandbox.FileSystem.File.ReadAllText(file))).Throws<System.Text.Json.JsonException>();
        await Assert.That(await OpenAsync(editor, options, file)).IsEqualTo(0);
        await Assert.That(sandbox.FileSystem.File.ReadAllText(file)).IsEqualTo("""{ "fixed": true }""");
        await Assert.That(await OpenAsync(editor, options, file)).IsEqualTo(0);
        await Assert.That(await OpenAsync(editor, EditorChange.None, file)).IsEqualTo(0);

        var log = editor.ReadLog();
        await Assert.That(string.Join(' ', log.Invocations.Select(invocation => $"{invocation.Run}#{invocation.Invocation}:{invocation.Step}")))
            .IsEqualTo("run-1#1:invalid run-1#2:replace run-1#3:replace run-2#1:none");
        await Assert.That(log.OfRun("run-1").Count).IsEqualTo(3);
        await Assert.That(editor.ReadLog(options).Count).IsEqualTo(3);
        await Assert.That(editor.ReadLog(options).Last().Step).IsEqualTo("replace");
    }

    // ReadLog(options) is the run of those options: Single() works in a test that runs edit several times with one editor.
    [Test]
    public async Task ReadLog_OfTheOptions_IsThatRun()
    {
        using var sandbox = new SelfTestSandbox();
        var editor = new FakeEditor(sandbox.WorkingDirectory, sandbox.FileSystem);
        var file = WriteDocument(sandbox, "config-runs.json");
        var first = editor.With(EditorChange.Merge("""{ "a": 2 }"""));
        var second = editor.With(EditorChange.None);

        await Assert.That(await OpenAsync(editor, first, file)).IsEqualTo(0);
        await Assert.That(await OpenAsync(editor, second, file)).IsEqualTo(0);

        await Assert.That(FakeEditor.RunOf(first)).IsEqualTo("run-1");
        await Assert.That(FakeEditor.RunOf(second)).IsEqualTo("run-2");
        await Assert.That(editor.ReadLog().Count).IsEqualTo(2);
        await Assert.That(editor.ReadLog(first).Single().WrittenJson()!["a"]!.GetValue<int>()).IsEqualTo(2);
        var none = editor.ReadLog(second).Single();
        await Assert.That(none.Step).IsEqualTo("none");
        await Assert.That(none.OriginalJson()!["a"]!.GetValue<int>()).IsEqualTo(2);
        await Assert.That(() => editor.ReadLog(new SformRunOptions())).Throws<ArgumentException>();
    }

    // Two editors of one test (one per home) keep their own folder, log and change files: the first editor's run does the
    // first editor's change even after the second wrote its own for its run-1.
    [Test]
    public async Task TwoEditors_KeepTheirOwnFiles()
    {
        using var sandbox = new SelfTestSandbox();
        var first = new FakeEditor(sandbox.WorkingDirectory, sandbox.FileSystem);
        var second = new FakeEditor(sandbox.WorkingDirectory, sandbox.FileSystem);
        var file = WriteDocument(sandbox, "config-two.json");
        var firstOptions = first.With(EditorChange.Merge("""{ "a": "first" }"""));
        _ = second.With(EditorChange.Merge("""{ "a": "second" }"""));

        await Assert.That(await OpenAsync(first, firstOptions, file)).IsEqualTo(0);

        await Assert.That(JsonNode.Parse(sandbox.FileSystem.File.ReadAllText(file))!["a"]!.GetValue<string>()).IsEqualTo("first");
        await Assert.That(first.Directory).IsNotEqualTo(second.Directory);
        await Assert.That(first.LogPath).IsNotEqualTo(second.LogPath);
        await Assert.That(first.ReadLog().Count).IsEqualTo(1);
        await Assert.That(second.ReadLog().Count).IsEqualTo(0);
    }

    // A build whose path has a space (a checkout under "D:\My Projects", where a data volume usually has no 8.3 names, or
    // anywhere on Linux): its output is copied to a folder without a space, and that apphost edits like the build's.
    [Test]
    public async Task BuildPathWithASpace_IsCopiedToAFolderWithout()
    {
        using var sandbox = new SelfTestSandbox();
        var fileSystem = sandbox.FileSystem;
        var built = fileSystem.Path.GetDirectoryName(FakeEditor.RecordedPath!)!;
        var spaced = fileSystem.Path.Combine(sandbox.WorkingDirectory, "a build with spaces");
        fileSystem.Directory.CreateDirectory(spaced);
        foreach (var source in fileSystem.Directory.EnumerateFiles(built))
        {
            fileSystem.File.Copy(source, fileSystem.Path.Combine(spaced, fileSystem.Path.GetFileName(source)));
        }

        var command = FakeEditor.ResolveCommand(fileSystem, fileSystem.Path.Combine(spaced, fileSystem.Path.GetFileName(FakeEditor.RecordedPath!)));
        try
        {
            await Assert.That(command).DoesNotContain(" ");
            await Assert.That(command).StartsWith(FakeEditor.CopiesRoot(fileSystem));
            await Assert.That(FakeEditor.ResolveCommand(fileSystem, fileSystem.Path.Combine(spaced, fileSystem.Path.GetFileName(FakeEditor.RecordedPath!)))).IsEqualTo(command);

            var editor = new FakeEditor(sandbox.WorkingDirectory, fileSystem, command);
            var file = WriteDocument(sandbox, "config-copy.json");
            await Assert.That(await OpenAsync(editor, EditorChange.Merge("""{ "a": 5 }"""), file)).IsEqualTo(0);
            await Assert.That(JsonNode.Parse(fileSystem.File.ReadAllText(file))!["a"]!.GetValue<int>()).IsEqualTo(5);
        }
        finally
        {
            var copy = fileSystem.Path.GetDirectoryName(command)!;
            if (copy.StartsWith(FakeEditor.CopiesRoot(fileSystem), StringComparison.OrdinalIgnoreCase) && fileSystem.Directory.Exists(copy))
            {
                fileSystem.Directory.Delete(copy, recursive: true);
            }
        }
    }

    // DOTNET_ROOT for the apphost only where it helps: none when the machine has its own (the runner passes it on), else a
    // real installation (host/fxr present) with links followed, never the folder of a link such as /usr/bin.
    [Test]
    public async Task RuntimeRoot_IsARealInstallationOrNone()
    {
        var fileSystem = new System.IO.Abstractions.FileSystem();
        var root = FakeEditor.RuntimeRoot(fileSystem);

        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 })
        {
            await Assert.That(root).IsNull();
            return;
        }

        if (root is not null)
        {
            await Assert.That(fileSystem.Directory.Exists(fileSystem.Path.Combine(root, "host", "fxr"))).IsTrue();
        }

        var options = new FakeEditor(fileSystem.Path.GetTempPath(), fileSystem).With(EditorChange.None);
        await Assert.That(options.Environment.GetValueOrDefault("DOTNET_ROOT")).IsEqualTo(root);
    }

    // A test that forgot the change: a clear failure (sform would say "Editor exited with code 65." and end with 1).
    [Test]
    public async Task WithoutAMode_FailsWithTheReason()
    {
        using var sandbox = new SelfTestSandbox();
        var editor = new FakeEditor(sandbox.WorkingDirectory, sandbox.FileSystem);
        var file = WriteDocument(sandbox, "config-none.json");
        var options = new SformRunOptions { Environment = { [FakeEditor.LogVariable] = editor.LogPath } };

        var (exitCode, stdOut, stdErr) = await StartAsync(editor.Command, options, file);

        await Assert.That(exitCode).IsEqualTo(65);
        await Assert.That(stdOut).IsEmpty();
        await Assert.That(stdErr).Contains("CLI_EDITOR_MODE is not set");
        await Assert.That(editor.ReadLog().Single().Error!).Contains("CLI_EDITOR_MODE is not set");
        await Assert.That(sandbox.FileSystem.File.ReadAllText(file)).IsEqualTo(Document);
    }

    // sform quotes the path, which may contain spaces (a temporary folder of a user with a space in the name).
    [Test]
    public async Task FileWithSpaces_IsOneArgument()
    {
        using var sandbox = new SelfTestSandbox();
        var editor = new FakeEditor(sandbox.WorkingDirectory, sandbox.FileSystem);
        var folder = sandbox.FileSystem.Path.Combine(sandbox.WorkingDirectory, "a folder with spaces");
        sandbox.FileSystem.Directory.CreateDirectory(folder);
        var file = sandbox.FileSystem.Path.Combine(folder, "config key.json");
        sandbox.FileSystem.File.WriteAllText(file, Document);

        var (exitCode, stdOut, _) = await StartAsync(editor.Command, editor.With(EditorChange.Merge("""{ "a": 3 }""")), file);

        await Assert.That(exitCode).IsEqualTo(0);
        await Assert.That(stdOut).IsEmpty();
        await Assert.That(JsonNode.Parse(sandbox.FileSystem.File.ReadAllText(file))!["a"]!.GetValue<int>()).IsEqualTo(3);
        await Assert.That(editor.ReadLog().Single().Arguments.Count).IsEqualTo(1);
    }

    private static string WriteDocument(SelfTestSandbox sandbox, string name)
    {
        var file = sandbox.FileSystem.Path.Combine(sandbox.WorkingDirectory, name);
        sandbox.FileSystem.File.WriteAllText(file, Document);
        return file;
    }

    private static Task<int> OpenAsync(FakeEditor editor, EditorChange change, string file)
    {
        return OpenAsync(editor, editor.With(change), file);
    }

    private static async Task<int> OpenAsync(FakeEditor editor, SformRunOptions options, string file)
    {
        var (exitCode, stdOut, _) = await StartAsync(editor.Command, options, file);
        await Assert.That(stdOut).IsEmpty();
        return exitCode;
    }

    // As EditorService.OpenFileInEditorAsync starts a custom editor: FileName the command, Arguments the quoted path, no
    // shell, no window; the variables of the run over the tests' environment (sform's would be built from scratch).
    private static async Task<(int ExitCode, string StdOut, string StdErr)> StartAsync(string command, SformRunOptions options, string file)
    {
        var start = new ProcessStartInfo
        {
            FileName = command,
            Arguments = $"\"{file}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var (name, value) in options.Environment)
        {
            start.Environment[name] = value;
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{command} did not start.");
        var stdOut = process.StandardOutput.ReadToEndAsync();
        var stdErr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, await stdOut, await stdErr);
    }
}
