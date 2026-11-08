using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Cli.Output.Exceptions;
using _42.Platform.Cli.UnitTests.Commands;
using Moq;
using Newtonsoft.Json.Linq;
using Sharprompt;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Services;

// EditJsonAsync on redirected input: the editor runs (a configured program needs no terminal), but nothing asks
// afterwards. An editor that leaves invalid JSON is not reopened, and a confirmation of the changes stops the run.
public class EditorServiceRedirectedInputTests
{
    private readonly MockFileSystem _fileSystem = new();
    private readonly RecordingConsole _console = new RecordingConsole().UseInnerConsole(redirectedInput: true);
    private readonly JObject _original = new() { ["name"] = "billing" };

    [Fact]
    public async Task InvalidJson_OnRedirectedInput_IsNotReopened_AndNothingIsSaved()
    {
        var editor = new ScriptedEditor(_fileSystem, ["{ \"name\": "]);

        var result = await editor.EditJsonAsync(_console.Object, ScriptedEditor.Vim, _original, "config-rst.billing", false);

        result.Edited.ShouldBeNull();
        result.ExitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        editor.Opened.Count.ShouldBe(1);
        _console.Lines.ShouldContain(line => line.StartsWith("! Invalid JSON: "));
        _console.Lines.ShouldContain("Nothing was saved.");
        _console.Mock.Verify(console => console.Confirm(It.IsAny<ConfirmOptions>()), Times.Never);
        _fileSystem.AllFiles.ShouldBeEmpty();
    }

    [Fact]
    public async Task Changes_OnRedirectedInput_StopAtTheConfirmation()
    {
        var editor = new ScriptedEditor(_fileSystem, ["""{ "name": "invoicing" }"""]);

        var exception = await Should.ThrowAsync<InteractionNeededException>(
            editor.EditJsonAsync(_console.Object, ScriptedEditor.Vim, _original, "config-rst.billing", false, confirm: true));

        exception.ExitCode.ShouldBe(ExitCodes.WARNING_INTERACTION_NEEDED);
        _console.ErrorLines.ShouldBe(
        [
            "! sform cannot ask for the confirmation of the changes: the input is redirected.",
            "  Pass -y|--yes to save them without asking.",
        ]);
        _console.Mock.Verify(console => console.Confirm(It.IsAny<ConfirmOptions>()), Times.Never);
        _fileSystem.AllFiles.ShouldBeEmpty();
    }

    // -y of the edit commands: confirm is false, and the edited document is returned.
    [Fact]
    public async Task Changes_WithoutAConfirmation_AreReturned()
    {
        var editor = new ScriptedEditor(_fileSystem, ["""{ "name": "invoicing" }"""]);

        var result = await editor.EditJsonAsync(_console.Object, ScriptedEditor.Vim, _original, "config-rst.billing", false, confirm: false);

        result.ExitCode.ShouldBe(ExitCodes.SUCCESS);
        result.Edited.ShouldNotBeNull()["name"]!.Value<string>().ShouldBe("invoicing");
        _console.ErrorText.ShouldBeEmpty();
    }

    [Fact]
    public async Task NoChange_OnRedirectedInput_IsNoWork()
    {
        var editor = new ScriptedEditor(_fileSystem, [null]);

        var result = await editor.EditJsonAsync(_console.Object, ScriptedEditor.Vim, _original, "config-rst.billing", false);

        result.Edited.ShouldBeNull();
        result.ExitCode.ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
        _console.ErrorText.ShouldBeEmpty();
        _fileSystem.AllFiles.Any().ShouldBeFalse();
    }
}
