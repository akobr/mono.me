using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Cli.Services;
using _42.Platform.Cli.UnitTests.Commands;
using Moq;
using Newtonsoft.Json.Linq;
using Sharprompt;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Services;

// The loop of EditJsonAsync with a stand-in for the editor (ScriptedEditor).
public class EditorServiceTests
{
    private const string RetryQuestion = "Would you like to re-open the editor to fix the issue";
    private const string SaveQuestion = "Do you want to save these changes";

    private readonly MockFileSystem _fileSystem = new();
    private readonly RecordingConsole _console = new();
    private readonly List<string> _questions = [];
    private readonly Queue<bool> _answers = new();

    public EditorServiceTests()
    {
        _console.Mock
            .Setup(console => console.Confirm(It.IsAny<ConfirmOptions>()))
            .Returns((ConfirmOptions options) =>
            {
                _questions.Add(options.Message);
                return _answers.Count > 0 ? _answers.Dequeue() : true;
            });
    }

    // A read keeps date-like strings as stored (ApiSdk.partial.cs), so an untouched file must compare equal.
    [Fact]
    public async Task AnUntouchedDocumentWithDateLikeStrings_HasNoChanges()
    {
        var editor = Editor((string?)null);

        var result = await editor.EditJsonAsync(_console.Object, ScriptedEditor.Vim, Dated(), "config-x", false);

        result.Edited.ShouldBeNull();
        result.ExitCode.ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
        _console.Lines.ShouldContain("No changes detected.");
        _questions.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnEditedDateLikeString_StaysTheStringAsWritten()
    {
        var editor = Editor("""{ "since": "2026-10-24T10:00:00+05:30", "at": "2026-12-01T00:00:00Z", "other": 1 }""");

        var result = await editor.EditJsonAsync(_console.Object, ScriptedEditor.Vim, Dated(), "config-x", false);

        result.ExitCode.ShouldBe(ExitCodes.SUCCESS);
        result.Edited!["since"]!.Type.ShouldBe(JTokenType.String);
        result.Edited["since"]!.Value<string>().ShouldBe("2026-10-24T10:00:00+05:30");
        result.Edited["at"]!.Value<string>().ShouldBe("2026-12-01T00:00:00Z");
        _questions.ShouldBe([SaveQuestion]);
    }

    [Fact]
    public async Task ADocumentTheCheckRejects_ReopensTheEditorOnTheSameText_BeforeTheQuestion()
    {
        var editor = Editor("""{ "bad": 1 }""", """{ "good": 1 }""");

        var result = await editor.EditJsonAsync(
            _console.Object,
            ScriptedEditor.Vim,
            new JObject { ["good"] = 0 },
            "annotation-x",
            false,
            check: document => document.ContainsKey("bad") ? JsonEditCheck.Invalid("'bad' is not allowed.") : JsonEditCheck.Valid(document));

        result.ExitCode.ShouldBe(ExitCodes.SUCCESS);
        result.Edited!["good"]!.Value<int>().ShouldBe(1);
        editor.Opened.Count.ShouldBe(2);
        editor.Opened[1].ShouldBe("""{ "bad": 1 }""");
        _console.Lines.ShouldContain(line => line.Contains("'bad' is not allowed.", StringComparison.Ordinal));
        _questions.ShouldBe([RetryQuestion, SaveQuestion]);
    }

    [Fact]
    public async Task ADocumentTheCheckRejects_IsAborted_WhenTheUserDoesNotFixIt()
    {
        var editor = Editor("""{ "bad": 1 }""");
        _answers.Enqueue(false);

        var result = await editor.EditJsonAsync(_console.Object, ScriptedEditor.Vim, new JObject(), "annotation-x", true, check: _ => JsonEditCheck.Invalid("Never valid."));

        result.Edited.ShouldBeNull();
        result.ExitCode.ShouldBe(ExitCodes.WARNING_ABORTED);
        editor.Opened.Count.ShouldBe(1);
        _console.Lines.ShouldContain("Edit aborted.");
    }

    // A change of the form only, which the check normalizes away, is no change: nothing is asked.
    [Fact]
    public async Task TheDocumentOfTheCheck_IsTheOneCompared_SoAChangeOfTheFormOnlyAsksNothing()
    {
        var editor = Editor("""{ "Labels": null }""");

        var result = await editor.EditJsonAsync(_console.Object, ScriptedEditor.Vim, new JObject { ["Labels"] = new JArray() }, "annotation-x", false, check: Normalize);

        result.Edited.ShouldBeNull();
        result.ExitCode.ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
        _questions.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheDocumentOfTheCheck_IsTheOneReturned()
    {
        var editor = Editor("""{ "Labels": null, "Title": "New" }""");

        var result = await editor.EditJsonAsync(_console.Object, ScriptedEditor.Vim, new JObject { ["Labels"] = new JArray() }, "annotation-x", false, check: Normalize);

        result.ExitCode.ShouldBe(ExitCodes.SUCCESS);
        result.Edited!["Labels"].ShouldBeOfType<JArray>();
        result.Edited["Title"]!.Value<string>().ShouldBe("New");
    }

    // The diff shows what the caller will save: the document of the check, not the text of the file.
    [Fact]
    public async Task TheDocumentOfTheCheck_IsTheOneShown()
    {
        var editor = Editor("""{ "Labels": null, "Title": "New" }""");

        await editor.EditJsonAsync(_console.Object, ScriptedEditor.Vim, new JObject { ["Labels"] = new JArray() }, "annotation-x", false, check: Normalize);

        var (_, shown) = editor.Diffs.ShouldHaveSingleItem();
        JToken.DeepEquals(JObject.Parse(shown), new JObject { ["Labels"] = new JArray(), ["Title"] = "New" }).ShouldBeTrue();
    }

    [Theory]
    [InlineData("{ \"a\": ")]
    [InlineData("{ \"a\": 1 } []")]
    [InlineData("{ \"a\": 1 } { \"b\": 2 }")]
    [InlineData("{ \"a\": 1, \"a\": 2 }")]
    [InlineData("{ \"a\": { \"b\": 1, \"b\": 2 } }")]
    [InlineData("[]")]
    [InlineData("")]
    [InlineData("{ \"a\": undefined }")]
    [InlineData("{ \"a\": { \"b\": new Date(1) } }")]
    [InlineData("{ \"a\": [NaN] }")]
    [InlineData("{ \"a\": -Infinity }")]
    public async Task InvalidJson_ReopensTheEditor(string invalid)
    {
        var editor = Editor(invalid, """{ "a": 2 }""");

        var result = await editor.EditJsonAsync(_console.Object, ScriptedEditor.Vim, new JObject { ["a"] = 1 }, "config-x", false);

        result.ExitCode.ShouldBe(ExitCodes.SUCCESS);
        result.Edited!["a"]!.Value<int>().ShouldBe(2);
        editor.Opened.Count.ShouldBe(2);
        _console.Lines.ShouldContain(line => line.Contains("Invalid JSON", StringComparison.Ordinal));
    }

    [Fact]
    public void Comments_InsideAndAfterTheObject_AreAccepted()
    {
        var document = EditorService.ParseDocument("""
            // before
            {
              /* inside */ "a": 1, // after a member
              "b": [1, /* in an array */ 2]
            }
            // after the object
            """);

        document.ToString(Newtonsoft.Json.Formatting.None).ShouldBe("""{"a":1,"b":[1,2]}""");
    }

    // The configurations client reads 256 levels; the API stores up to 128.
    [Fact]
    public void ADeepDocument_IsRead()
    {
        var json = string.Concat(Enumerable.Repeat("{\"a\":", 100)) + "1" + new string('}', 100);

        EditorService.ParseDocument(json).ShouldNotBeNull();
    }

    [Fact]
    public async Task TheTemporaryFile_IsNamedSafely_AndDeleted()
    {
        var editor = Editor("""{ "a": 2 }""");

        await editor.EditJsonAsync(_console.Object, ScriptedEditor.Vim, new JObject { ["a"] = 1 }, "annotation-sbt.\"north\" %PATH% wind", false);

        var path = editor.Paths.ShouldHaveSingleItem();
        var name = _fileSystem.Path.GetFileName(path);
        name.ShouldStartWith("annotation-sbt._north___PATH__wind-");
        name.ShouldEndWith(".json");
        _fileSystem.FileExists(path).ShouldBeFalse();
    }

    [Fact]
    public async Task AFailedEditor_IsACrash()
    {
        var editor = new ScriptedEditor(_fileSystem, [null]) { ExitCode = 3 };

        var result = await editor.EditJsonAsync(_console.Object, ScriptedEditor.Vim, new JObject(), "config-x", false);

        result.Edited.ShouldBeNull();
        result.ExitCode.ShouldBe(ExitCodes.ERROR_CRASH);
    }

    private static JsonEditCheck Normalize(JObject document)
    {
        var normalized = (JObject)document.DeepClone();

        if (normalized["Labels"] is { Type: JTokenType.Null })
        {
            normalized["Labels"] = new JArray();
        }

        return JsonEditCheck.Valid(normalized);
    }

    private static JObject Dated()
    {
        return new JObject
        {
            ["since"] = "2026-10-24T10:00:00.000+02:00",
            ["at"] = "2026-12-01T00:00:00Z",
            ["nested"] = new JObject { ["at"] = "2026-10-24T10:00:00+05:30" },
        };
    }

    private ScriptedEditor Editor(params string?[] edits)
    {
        return new ScriptedEditor(_fileSystem, edits);
    }
}
