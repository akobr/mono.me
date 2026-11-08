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

    [Theory]
    [InlineData("{ \"a\": ")]
    [InlineData("{ \"a\": 1 } []")]
    [InlineData("{ \"a\": 1 } { \"b\": 2 }")]
    [InlineData("{ \"a\": 1, \"a\": 2 }")]
    [InlineData("{ \"a\": { \"b\": 1, \"b\": 2 } }")]
    [InlineData("[]")]
    [InlineData("")]
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
