using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using _42.Platform.Cli.AutomationTests.Editing;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Platform.Cli.AutomationTests.Running.Terminal;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;

namespace _42.Platform.Cli.AutomationTests.Tests.Annotations;

// Edit: sform story edit (StorytellerEditCommand) opens the editable members of an annotation in the editor of the home
// (here the fake editor, mode E) and, after "Do you want to save these changes" (it has no -y, so it needs a terminal:
// mode T), writes the saved document whole: a replace, conditioned on the version it opened (If-Match) or on the key
// still missing (If-None-Match: *). A missing key is created, a deleted one asks first, and a failed save keeps the edit
// in a file with the command that saves it anyway (story set -i <file> --replace).
[Area(Area.Annotations)]
public sealed class AnnotationEditorTests : CliTest
{
    // The questions and messages of story edit (StorytellerEditCommand, EditorService.EditJsonAsync).
    internal const string SaveQuestion = "Do you want to save these changes";
    internal const string ReopenQuestion = "Would you like to re-open the editor to fix the issue";
    internal const string ChoiceQuestion = "What do you want to do";

    // ANN-EDT-01: story edit opens the annotation in the editor; the confirmed document replaces its title, labels and
    // values.
    // As an Editor, <rst> is set with -l old -c keep=1 -c drop=2. In a terminal, story edit <rst> opens the fake editor on
    // the document of <rst> (Title null, Labels [old], Values keep 1 and drop 2); the editor sets the title, the labels a
    // and b, removes drop and adds added=x; sform shows the changes, Yes to "Do you want to save these changes" prints
    // "Annotation '<rst>' has been saved." and ends with 0. story get <rst> has the title, the labels a and b only and the
    // values keep and added only: the document replaced the content.
    [Test]
    [TestCase(464)]
    [UserRole(Role.Editor)]
    [Terminal]
    [Covers("story edit", "arg0")]
    public async Task Story_Edit_SavesTitleLabelsAndValues()
    {
        var key = CliData.ResponsibilityKey();
        var title = CliData.Value();
        await Sform(InProject("story", "set", key, "-l", "old", "-c", "keep=1", "-c", "drop=2")).ShouldSucceed();
        var editor = await UseEditorAsync();
        var options = editor.With(EditorChange.Merge(new JsonObject
        {
            ["Title"] = title,
            ["Labels"] = new JsonArray("a", "b"),
            ["Values"] = new JsonObject { ["drop"] = null, ["added"] = "x" },
        }));

        await using (var terminal = await StartSformInTerminal(options, InProject("story", "edit", key)))
        {
            await terminal.ExpectAsync("Changes");
            await AnswerSaveAsync(terminal, save: true);
            await terminal.ExpectAsync($"Annotation '{key}' has been saved.");
            (await terminal.WaitForExitAsync()).ShouldSucceed();
        }

        var opened = editor.ReadLog(options).Single().OriginalJson()!;
        await Assert.That(opened["Title"]).IsNull();
        await Assert.That(Strings(opened["Labels"])).IsEquivalentTo(new[] { "old" });
        await Assert.That(opened["Values"]?["keep"]?.GetValue<double>()).IsEqualTo(1d);
        await Assert.That(opened["Values"]?["drop"]?.GetValue<double>()).IsEqualTo(2d);

        var annotation = (await Sform(InProject("story", "get", key))).ShouldSucceed().JsonNode();
        await Assert.That(annotation["Title"]?.GetValue<string>()).IsEqualTo(title);
        await Assert.That(Strings(annotation["Labels"])).IsEquivalentTo(new[] { "a", "b" });
        await Assert.That(Names(annotation["Values"])).IsEquivalentTo(new[] { "keep", "added" });
        await Assert.That(annotation["Values"]?["keep"]?.GetValue<double>()).IsEqualTo(1d);
        await Assert.That(annotation["Values"]?["added"]?.GetValue<string>()).IsEqualTo("x");
    }

    // ANN-EDT-02: An edit without changes ends with exit 42, and declining to save ends with exit 40; neither changes the
    // annotation.
    // As an Editor, <rst> has a label. In a terminal, story edit <rst> with the editor leaving the file prints "No changes
    // detected." and ends with 42 without asking; story edit <rst> with the editor setting a title, answered No at "Do you
    // want to save these changes", prints "Edit aborted." and ends with 40. story get <rst> has no title and the label, and
    // the history has only the Created entry.
    [Test]
    [TestCase(465)]
    [UserRole(Role.Editor)]
    [Terminal]
    [Covers("story edit")]
    public async Task Story_Edit_NoChangeOrDeclined()
    {
        var key = CliData.ResponsibilityKey();
        var label = CliData.LabelName();
        await Sform(InProject("story", "set", key, "-l", label)).ShouldSucceed();
        var editor = await UseEditorAsync();

        var unchanged = editor.With(EditorChange.None);
        await using (var terminal = await StartSformInTerminal(unchanged, InProject("story", "edit", key)))
        {
            await terminal.ExpectAsync("No changes detected.");
            (await terminal.WaitForExitAsync()).ShouldExitWith(CliExit.NoWorkNeeded).ShouldNotContain(SaveQuestion);
        }

        await Assert.That(editor.ReadLog(unchanged).Single().Step).IsEqualTo("none");

        var declined = editor.With(EditorChange.Merge(new JsonObject { ["Title"] = CliData.Value() }));
        await using (var terminal = await StartSformInTerminal(declined, InProject("story", "edit", key)))
        {
            await AnswerSaveAsync(terminal, save: false);
            await terminal.ExpectAsync("Edit aborted.");
            (await terminal.WaitForExitAsync()).ShouldExitWith(CliExit.Aborted);
        }

        var annotation = (await Sform(InProject("story", "get", key))).ShouldSucceed().JsonNode();
        await Assert.That(annotation["Title"]).IsNull();
        await Assert.That(Strings(annotation["Labels"])).IsEquivalentTo(new[] { label });
        var history = (await Sform(InProject("story", "history", key, "--json"))).ShouldSucceed().JsonNode();
        await Assert.That(Actions(history)).IsEquivalentTo(new[] { "Created" });
    }

    // ANN-EDT-03: An invalid document re-opens the editor: invalid JSON and a member the check refuses are reported, and
    // the fixed document is saved.
    // As an Editor, <rst> has a label. In a terminal, story edit <rst>: the editor writes text that is not JSON ("Invalid
    // JSON: …" and "Would you like to re-open the editor to fix the issue"), after Yes a document with a title and the
    // member AnnotationKey ("'AnnotationKey' cannot be edited: …" and the question again), after Yes it removes
    // AnnotationKey; Yes at "Do you want to save these changes" saves it (exit 0). The editor ran three times, and story get
    // <rst> has the title and no label (the document replaced the content). Again with text that is not JSON and No to the
    // re-open question: "Edit aborted.", 40, and the title stays.
    [Test]
    [TestCase(466)]
    [UserRole(Role.Editor)]
    [Terminal]
    [Covers("story edit")]
    public async Task Story_Edit_InvalidDocument_ReopensEditor()
    {
        var key = CliData.ResponsibilityKey();
        var title = CliData.Value();
        await Sform(InProject("story", "set", key, "-l", CliData.LabelName())).ShouldSucceed();
        var editor = await UseEditorAsync();

        var fixedLater = editor.With(EditorChange.Sequence(
            EditorChange.Invalid,
            EditorChange.Replace(new JsonObject { ["Title"] = title, ["AnnotationKey"] = key }.ToJsonString()),
            EditorChange.Merge("""{ "AnnotationKey": null }""")));
        await using (var terminal = await StartSformInTerminal(fixedLater, InProject("story", "edit", key)))
        {
            await terminal.ExpectAsync("Invalid JSON:");
            await terminal.ExpectAsync(ReopenQuestion);
            await terminal.SendLineAsync("y");
            await terminal.ExpectAsync("'AnnotationKey' cannot be edited: the key decides the type, the names and the relations, and the server keeps the stamps.");
            await terminal.ExpectAsync(ReopenQuestion);
            await terminal.SendLineAsync("y");
            await AnswerSaveAsync(terminal, save: true);
            await terminal.ExpectAsync($"Annotation '{key}' has been saved.");
            (await terminal.WaitForExitAsync()).ShouldSucceed();
        }

        var steps = string.Join(", ", editor.ReadLog(fixedLater).Invocations.Select(invocation => invocation.Step));
        await Assert.That(steps).IsEqualTo("invalid, replace, merge");
        var saved = (await Sform(InProject("story", "get", key))).ShouldSucceed().JsonNode();
        await Assert.That(saved["Title"]?.GetValue<string>()).IsEqualTo(title);
        await Assert.That(Strings(saved["Labels"]).Count).IsEqualTo(0);

        var abandoned = editor.With(EditorChange.Invalid);
        await using (var terminal = await StartSformInTerminal(abandoned, InProject("story", "edit", key)))
        {
            await terminal.ExpectAsync("Invalid JSON:");
            await terminal.ExpectAsync(ReopenQuestion);
            await terminal.SendLineAsync("n");
            await terminal.ExpectAsync("Edit aborted.");
            (await terminal.WaitForExitAsync()).ShouldExitWith(CliExit.Aborted);
        }

        var kept = (await Sform(InProject("story", "get", key))).ShouldSucceed().JsonNode();
        await Assert.That(kept["Title"]?.GetValue<string>()).IsEqualTo(title);
    }

    // ANN-EDT-04: On redirected input story edit ends with exit 43 before any request and names the commands that edit
    // without a terminal.
    // As an Editor with the fake editor configured, story edit <rst> of a missing key without a terminal ends with 43 and
    // prints on stderr "! sform cannot ask for the edit of the annotation: the input or the output is redirected." and "Without a terminal:
    // sform story get <rst> -e <file>, edit the file, then sform story set <rst> -i <file> --replace."; the editor is not
    // started and story get <rst> finds nothing. With SFORM_general__baseUrl on a closed port of the loopback it ends with
    // 43 again (a request would end with 3).
    [Test]
    [TestCase(467)]
    [UserRole(Role.Editor)]
    [Covers("story edit")]
    public async Task Story_Edit_Redirected_NeedsTerminal()
    {
        var key = CliData.ResponsibilityKey();
        var hint = $"Without a terminal: sform story get {key} -e <file>, edit the file, then sform story set {key} -i <file> --replace.";
        var editor = await UseEditorAsync();

        var redirected = editor.With(EditorChange.Merge(new JsonObject { ["Title"] = CliData.Value() }));
        (await Sform(redirected, InProject("story", "edit", key)))
            .ShouldExitWith(CliExit.InteractionNeeded)
            .ShouldContain("! sform cannot ask for the edit of the annotation: the input or the output is redirected.", SformStream.StdErr)
            .ShouldContain(hint, SformStream.StdErr);
        await Assert.That(editor.ReadLog(redirected).Count).IsEqualTo(0);

        (await Sform(InProject("story", "get", key)))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"The annotation '{key}' has not been found.");

        var offline = editor.With(
            EditorChange.Merge(new JsonObject { ["Title"] = CliData.Value() }),
            new SformRunOptions { BaseUrl = $"http://127.0.0.1:{ClosedPort()}/api", Label = "against a closed port" });
        (await Sform(offline, InProject("story", "edit", key)))
            .ShouldExitWith(CliExit.InteractionNeeded)
            .ShouldContain("! sform cannot ask for the edit of the annotation: the input or the output is redirected.", SformStream.StdErr)
            .ShouldContain(hint, SformStream.StdErr);
        await Assert.That(editor.ReadLog(offline).Count).IsEqualTo(0);
    }

    // ANN-EDT-05: story edit of a missing key opens an empty document and creates the annotation; without changes it
    // creates nothing.
    // As an Editor, in a terminal, story edit <rst> of a missing key with the editor leaving the file prints "Annotation
    // '<rst>' does not exist yet, creating new.", "No changes detected." and "Nothing was created. To create it without
    // content: sform story set <rst>" and ends with 42; the editor got the empty document (Title null, Labels [], Values
    // {}), and story get <rst> finds nothing. Again with the editor setting a title and a label: "New document", Yes, and
    // "Annotation '<rst>' has been saved." (exit 0); story get has both, and the history one Created entry.
    [Test]
    [TestCase(468)]
    [UserRole(Role.Editor)]
    [Terminal]
    [Covers("story edit")]
    public async Task Story_Edit_MissingKey_Creates()
    {
        var key = CliData.ResponsibilityKey();
        var title = CliData.Value();
        var label = CliData.LabelName();
        var editor = await UseEditorAsync();

        var unchanged = editor.With(EditorChange.None);
        await using (var terminal = await StartSformInTerminal(unchanged, InProject("story", "edit", key)))
        {
            await terminal.ExpectAsync($"Annotation '{key}' does not exist yet, creating new.");
            await terminal.ExpectAsync("No changes detected.");
            await terminal.ExpectAsync($"Nothing was created. To create it without content: sform story set {key}");
            (await terminal.WaitForExitAsync()).ShouldExitWith(CliExit.NoWorkNeeded);
        }

        var empty = editor.ReadLog(unchanged).Single().OriginalJson()!;
        await Assert.That(empty["Title"]).IsNull();
        await Assert.That(Strings(empty["Labels"]).Count).IsEqualTo(0);
        await Assert.That(Names(empty["Values"]).Count).IsEqualTo(0);
        (await Sform(InProject("story", "get", key)))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"The annotation '{key}' has not been found.");

        var created = editor.With(EditorChange.Merge(new JsonObject { ["Title"] = title, ["Labels"] = new JsonArray(label) }));
        await using (var terminal = await StartSformInTerminal(created, InProject("story", "edit", key)))
        {
            await terminal.ExpectAsync($"Annotation '{key}' does not exist yet, creating new.");
            await terminal.ExpectAsync("New document");
            await AnswerSaveAsync(terminal, save: true);
            await terminal.ExpectAsync($"Annotation '{key}' has been saved.");
            (await terminal.WaitForExitAsync()).ShouldSucceed();
        }

        var annotation = (await Sform(InProject("story", "get", key))).ShouldSucceed().JsonNode();
        await Assert.That(annotation["Title"]?.GetValue<string>()).IsEqualTo(title);
        await Assert.That(Strings(annotation["Labels"])).IsEquivalentTo(new[] { label });
        var history = (await Sform(InProject("story", "history", key, "--json"))).ShouldSucceed().JsonNode();
        await Assert.That(Actions(history)).IsEquivalentTo(new[] { "Created" });
    }

    // ANN-EDT-06: story edit of a deleted annotation asks first: Cancel ends with exit 40, Restore and edit keeps its
    // configuration, Create new starts empty.
    // As an Editor, <rst> has a title (story set -i) and a configuration (config set -x cli_probe=<value>), and is deleted.
    // In a terminal, story edit <rst> lists "This annotation is deleted:" and asks "What do you want to do": Cancel prints
    // "Nothing was changed." and ends with 40 without the editor, and <rst> stays deleted. Restore and edit opens the
    // deleted annotation's document (its title); the editor changes the title, Yes, then "Restored <rst>." and "Annotation
    // '<rst>' has been saved." (exit 0): story get has the new title, the configuration still has cli_probe, and the
    // history has Deleted and Restored. After another delete, Create new opens an empty document, the editor sets a title,
    // Yes saves it without "Restored": the title, and the configuration no longer has cli_probe.
    [Test]
    [TestCase(469)]
    [UserRole(Role.Editor)]
    [Terminal]
    [Covers("story edit")]
    public async Task Story_Edit_DeletedKey_RestoreCreateOrCancel()
    {
        var key = CliData.ResponsibilityKey();
        var title = CliData.Value();
        var edited = CliData.Value();
        var recreatedTitle = CliData.Value();
        var probe = CliData.Value();
        await WriteFileAsync("title.json", new JsonObject { ["Title"] = title }.ToJsonString());
        await Sform(InProject("story", "set", key, "-i", "title.json")).ShouldSucceed();
        await Sform(InProject("story", "config", "set", key, "-x", $"cli_probe={probe}")).ShouldSucceed();
        await Sform(InProject("story", "delete", key)).ShouldSucceed();
        var editor = await UseEditorAsync();

        var cancelled = editor.With(EditorChange.Merge(new JsonObject { ["Title"] = edited }));
        await using (var terminal = await StartSformInTerminal(cancelled, InProject("story", "edit", key)))
        {
            await terminal.ExpectAsync("This annotation is deleted:");
            await terminal.ExpectAsync(ChoiceQuestion);
            await terminal.SendKeyAsync(TerminalKey.Down, 2);
            await terminal.SendKeyAsync(TerminalKey.Enter);
            await terminal.ExpectAsync("Nothing was changed.");
            (await terminal.WaitForExitAsync()).ShouldExitWith(CliExit.Aborted);
        }

        await Assert.That(editor.ReadLog(cancelled).Count).IsEqualTo(0);
        (await Sform(InProject("story", "get", key)))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"The annotation '{key}' is deleted");

        var restored = editor.With(EditorChange.Merge(new JsonObject { ["Title"] = edited }));
        await using (var terminal = await StartSformInTerminal(restored, InProject("story", "edit", key)))
        {
            await terminal.ExpectAsync(ChoiceQuestion);
            await terminal.SendKeyAsync(TerminalKey.Enter);
            await AnswerSaveAsync(terminal, save: true);
            await terminal.ExpectAsync($"Restored {key}.");
            await terminal.ExpectAsync($"Annotation '{key}' has been saved.");
            (await terminal.WaitForExitAsync()).ShouldSucceed();
        }

        await Assert.That(editor.ReadLog(restored).Single().OriginalJson()!["Title"]?.GetValue<string>()).IsEqualTo(title);
        var annotation = (await Sform(InProject("story", "get", key))).ShouldSucceed().JsonNode();
        await Assert.That(annotation["Title"]?.GetValue<string>()).IsEqualTo(edited);
        var content = (await Sform(InProject("story", "config", key, "--content-only"))).ShouldSucceed().JsonNode();
        await Assert.That(content["cli_probe"]?.GetValue<string>()).IsEqualTo(probe);
        var actions = Actions((await Sform(InProject("story", "history", key, "--json"))).ShouldSucceed().JsonNode());
        await Assert.That(actions).Contains("Deleted");
        await Assert.That(actions).Contains("Restored");

        await Sform(InProject("story", "delete", key)).ShouldSucceed();
        var recreated = editor.With(EditorChange.Merge(new JsonObject { ["Title"] = recreatedTitle }));
        await using (var terminal = await StartSformInTerminal(recreated, InProject("story", "edit", key)))
        {
            await terminal.ExpectAsync(ChoiceQuestion);
            await terminal.SendKeyAsync(TerminalKey.Down);
            await terminal.SendKeyAsync(TerminalKey.Enter);
            await AnswerSaveAsync(terminal, save: true);
            await terminal.ExpectAsync($"Annotation '{key}' has been saved.");
            (await terminal.WaitForExitAsync()).ShouldSucceed().ShouldNotContain("Restored ");
        }

        await Assert.That(editor.ReadLog(recreated).Single().OriginalJson()!["Title"]).IsNull();
        var created = (await Sform(InProject("story", "get", key))).ShouldSucceed().JsonNode();
        await Assert.That(created["Title"]?.GetValue<string>()).IsEqualTo(recreatedTitle);
        var emptied = (await Sform(InProject("story", "config", key, "--content-only"))).ShouldSucceed().JsonNode();
        await Assert.That(emptied["cli_probe"]).IsNull();
    }

    // ANN-EDT-07: A change by another user while the edit waits for its confirmation is not overwritten: exit 2, and the
    // edit is kept in a file.
    // As an Editor, <rst> is set with -c a=1, and another Editor of the project is signed in. In a terminal, with sform's
    // temporary folder in the working directory, story edit <rst> opens the editor, which sets a title; while sform waits at
    // "Do you want to save these changes", the other Editor runs story set <rst> -c b=2; Yes: the write of the version the
    // edit opened is refused (412), and sform says "The annotation '<rst>' changed while you edited it, so your edit was not
    // saved.", "Your edit is in <file>. To save it anyway: sform story set <rst> -i "<file>" --replace" and ends with 2.
    // story get <rst> has no title and both a=1 and b=2. The file holds the edited document (the title, a=1, no b), and
    // story set <rst> -i <file> --replace saves it: the title, a=1, and no b.
    [Test]
    [TestCase(470)]
    [UserRole(Role.Editor)]
    [Terminal]
    [Covers("story edit")]
    public async Task Story_Edit_ConcurrentChange_NotOverwritten()
    {
        var key = CliData.ResponsibilityKey();
        var title = CliData.Value();
        await Sform(InProject("story", "set", key, "-c", "a=1")).ShouldSucceed();

        // signed in before the edit waits at its question
        var other = await AsAnotherUserAsync(EditingUser);
        await other.Sform(InProject("story", "get", key)).ShouldSucceed();

        var temporary = TemporaryFolder();
        var editor = await UseEditorAsync();
        var options = editor.With(EditorChange.Merge(new JsonObject { ["Title"] = title }), InTemporaryFolder(temporary));
        await using (var terminal = await StartSformInTerminal(options, InProject("story", "edit", key)))
        {
            await terminal.ExpectAsync(SaveQuestion);
            (await other.Sform(InProject("story", "set", key, "-c", "b=2"))).ShouldSucceed();
            await terminal.SendLineAsync("y");
            await terminal.ExpectAsync($"The annotation '{key}' changed while you edited it, so your edit was not saved.");
            await terminal.ExpectAsync("Your edit is in");
            await terminal.ExpectAsync($"To save it anyway: sform story set {key} -i");
            await terminal.ExpectAsync("--replace");
            (await terminal.WaitForExitAsync()).ShouldExitWith(CliExit.WrongInput);
        }

        var current = (await Sform(InProject("story", "get", key))).ShouldSucceed().JsonNode();
        await Assert.That(current["Title"]).IsNull();
        await Assert.That(current["Values"]?["a"]?.GetValue<double>()).IsEqualTo(1d);
        await Assert.That(current["Values"]?["b"]?.GetValue<double>()).IsEqualTo(2d);

        var file = KeptEdit(temporary, key);
        var document = JsonNode.Parse(await FileSystem.File.ReadAllTextAsync(file))!;
        await Assert.That(document["Title"]?.GetValue<string>()).IsEqualTo(title);
        await Assert.That(Names(document["Values"])).IsEquivalentTo(new[] { "a" });

        (await Sform(InProject("story", "set", key, "-i", file, "--replace"))).ShouldSucceed();
        var saved = (await Sform(InProject("story", "get", key))).ShouldSucceed().JsonNode();
        await Assert.That(saved["Title"]?.GetValue<string>()).IsEqualTo(title);
        await Assert.That(Names(saved["Values"])).IsEquivalentTo(new[] { "a" });
        await Assert.That(saved["Values"]?["a"]?.GetValue<double>()).IsEqualTo(1d);
    }

    // ANN-EDT-08: A Reader's saved edit is refused with exit 44, and the edit is kept in a file.
    // As a Reader of the project (Target:RoleProjects:Reader, or a Reader of Target:Project), in a terminal with sform's
    // temporary folder in the working directory, story edit <rst> of a missing key says "Annotation '<rst>' does not exist
    // yet, creating new."; the editor sets a title, and Yes ends with 44 (the server's refusal) and "Your edit is in
    // <file>. To save it anyway: sform story set <rst> -i "<file>" --replace". The file holds the title, and story get
    // <rst> finds nothing.
    [Test]
    [TestCase(471)]
    [UserRole(Role.Reader)]
    [Terminal]
    [Covers("story edit")]
    public async Task Story_Edit_AsReader_RefusedAndKept()
    {
        var key = CliData.ResponsibilityKey();
        var title = CliData.Value();
        var temporary = TemporaryFolder();
        var editor = await UseEditorAsync();
        var options = editor.With(EditorChange.Merge(new JsonObject { ["Title"] = title }), InTemporaryFolder(temporary));

        await using (var terminal = await StartSformInTerminal(options, InProject("story", "edit", key)))
        {
            await terminal.ExpectAsync($"Annotation '{key}' does not exist yet, creating new.");
            await AnswerSaveAsync(terminal, save: true);
            await terminal.ExpectAsync("Your edit is in");
            await terminal.ExpectAsync($"To save it anyway: sform story set {key} -i");
            (await terminal.WaitForExitAsync()).ShouldExitWith(CliExit.Unauthorized);
        }

        var file = KeptEdit(temporary, key);
        var document = JsonNode.Parse(await FileSystem.File.ReadAllTextAsync(file))!;
        await Assert.That(document["Title"]?.GetValue<string>()).IsEqualTo(title);
        (await Sform(InProject("story", "get", key)))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"The annotation '{key}' has not been found.");
    }

    // Answers "Do you want to save these changes" with Yes or No.
    internal static async Task AnswerSaveAsync(SformProcess terminal, bool save)
    {
        ArgumentNullException.ThrowIfNull(terminal);
        await terminal.ExpectAsync(SaveQuestion);
        await terminal.SendLineAsync(save ? "y" : "n");
    }

    // The options of a run whose temporary folder (TEMP, TMP, TMPDIR) is the folder: sform writes the editor's file there,
    // and the edit it keeps after a failed save, so both go with the working directory. The terminal is 1000 columns wide,
    // so the line that names the kept file (its path twice) is not wrapped.
    private static SformRunOptions InTemporaryFolder(string folder)
    {
        return new SformRunOptions
        {
            Environment = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["TEMP"] = folder,
                ["TMP"] = folder,
                ["TMPDIR"] = folder,
            },
            TerminalColumns = 1000,
        };
    }

    // A port of the loopback that nothing listens on: one the system just gave out and that was closed again.
    private static int ClosedPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    // The strings of a JSON array (Labels); empty for a missing one.
    private static List<string> Strings(JsonNode? array)
    {
        return (array as JsonArray)?.Select(item => item!.GetValue<string>()).ToList() ?? [];
    }

    // The member names of a JSON object (Values); empty for a missing one.
    private static List<string> Names(JsonNode? value)
    {
        return (value as JsonObject)?.Select(pair => pair.Key).ToList() ?? [];
    }

    // The actions of the entries of story history --json.
    private static List<string> Actions(JsonNode history)
    {
        return (history["Entries"] as JsonArray)?.Select(entry => entry!["Action"]!.GetValue<string>()).ToList() ?? [];
    }

    // The folder of the working directory that InTemporaryFolder names.
    private string TemporaryFolder()
    {
        var folder = FileSystem.Path.Combine(WorkingDirectory, "tmp");
        FileSystem.Directory.CreateDirectory(folder);
        return folder;
    }

    // The one file sform kept the edit of the key in (EditorService.CreateTempFilePath: annotation-<key>-<random>.json;
    // the editor's own file of the same name is deleted after the editor).
    private string KeptEdit(string folder, string key)
    {
        var files = FileSystem.Directory.GetFiles(folder, $"annotation-{key}-*.json");
        return files.Length == 1
            ? files[0]
            : throw new InvalidOperationException($"sform should have kept the edit of {key} in one file of {folder}, which has {(files.Length == 0 ? "none" : string.Join(", ", files))}.");
    }
}
