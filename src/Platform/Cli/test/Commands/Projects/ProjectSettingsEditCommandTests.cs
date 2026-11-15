using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Commands.Projects;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Services;
using Microsoft.Extensions.Options;
using Moq;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;
using SdkSettings = _42.Platform.Storyteller.Sdk.ProjectSettings;

namespace _42.Platform.Cli.UnitTests.Commands.Projects;

// sform project settings edit: the file shape in the editor, the shared rules as its check, a PUT under the version that
// was opened, and the edit kept in a file when it cannot be saved.
public class ProjectSettingsEditCommandTests
{
    private readonly ProjectSettingsFixture _fixture = new();
    private readonly Mock<IEditorService> _editor = new(MockBehavior.Strict);

    private JObject? _opened;

    [Fact]
    public async Task Edit_OpensTheFileShape_AndSavesUnderTheOpenedVersion()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        var sent = _fixture.SetupPut(ProjectSettingsFixture.Changed(version: 4));
        EditTo(document => document["Types"]!["sbt"]!["Label"] = "Client");

        var exitCode = await Command().OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _opened!["Format"]!.Value<string>().ShouldBe(_42.Platform.Storyteller.ProjectSettings.FileFormat);
        _opened.ContainsKey("Version").ShouldBeFalse();
        _opened["Types"]!["sbt"]!["Label"]!.Value<string>().ShouldBe("Company");

        var (body, ifMatch) = sent.ShouldHaveSingleItem();
        ifMatch.ShouldBe("\"3\"");
        body.Types["sbt"].Label.ShouldBe("Client");
        body.Types["sbt"].Color.ShouldBe("pink");
        body.DefaultTimeZone.ShouldBe("Europe/Prague");
        _fixture.Lines.ShouldBe(["Saved the project settings, version 4."]);
        _fixture.VerifyRemembered(4);
    }

    [Fact]
    public async Task Edit_IsNormalizedByTheRules_BeforeTheDiff()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        _fixture.SetupPut(ProjectSettingsFixture.Changed(version: 4));
        JObject? checkedDocument = null;
        EditTo(document => document["DefaultTimeZone"] = "america/phoenix", result => checkedDocument = result);

        await Command().OnExecuteAsync();

        checkedDocument!["DefaultTimeZone"]!.Value<string>().ShouldBe("America/Phoenix");
    }

    [Fact]
    public async Task Edit_ThatBreaksTheRules_ListsTheProblems_AndKeepsTheEdit()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        EditTo(document => document["Types"]!["sbt"]!["Color"] = "pinkish");

        var exitCode = await Command().OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Lines[0].ShouldStartWith("! $.Types.sbt.Color: 'pinkish' is not a color of the catalog");
        var kept = KeptFile();
        JObject.Parse(_fixture.FileSystem.File.ReadAllText(kept))["Types"]!["sbt"]!["Color"]!.Value<string>().ShouldBe("pinkish");
        _fixture.Lines[^1].ShouldBe($"Your edit is in {kept}. Fix it, then save it with: sform project settings set -p org1.proj1 -i \"{kept}\" --replace");
    }

    [Fact]
    public async Task Edit_OfSettingsThatChangedMeanwhile_KeepsTheEdit()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        _fixture.SetupPut(ProjectSettingsFixture.PreconditionFailed(5));
        EditTo(document => document["DateFormat"] = "mm/dd/yyyy");

        var exitCode = await Command().OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        var kept = KeptFile();
        _fixture.Lines.ShouldBe([
            "! The project settings changed meanwhile; they are at version 5.",
            $"Your edit is in {kept}. To save it anyway: sform project settings set -p org1.proj1 -i \"{kept}\" --replace",
        ]);
        _fixture.VerifyNothingRemembered();
    }

    [Fact]
    public async Task Edit_Refused_PrintsTheServerMessage_AndKeepsTheEdit()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        _fixture.SetupPut(ProjectSettingsFixture.Forbidden());
        EditTo(document => document["DateFormat"] = "mm/dd/yyyy");

        var exitCode = await Command().OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        _fixture.Lines[0].ShouldBe("! Changing the project settings needs a user with the Administrator role.");
        _fixture.Lines[^1].ShouldStartWith("Your edit is in ");
    }

    [Fact]
    public async Task Edit_ThatTheServerSaysChangedNothing_SaysTheVersionStays()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        _fixture.SetupPut(ProjectSettingsFixture.Changed(version: 3));
        EditTo(document => document["DateFormat"] = "mm/dd/yyyy");

        var exitCode = await Command().OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
        _fixture.Lines.ShouldBe(["The project settings did not change; they stay at version 3."]);
    }

    [Fact]
    public async Task Edit_WithoutChanges_EndsAsTheEditorSays()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        SetupEditor((_, _) => new JsonEditResult(null, ExitCodes.WARNING_NO_WORK_NEEDED));

        var exitCode = await Command().OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
        _fixture.FileSystem.AllFiles.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Edit_WithoutATerminal_NeedsOne(bool inputRedirected, bool outputRedirected)
    {
        _fixture.Console.UseInnerConsole(redirectedInput: inputRedirected);
        var inner = Mock.Get(_fixture.Console.Object.Console);
        inner.SetupGet(console => console.IsOutputRedirected).Returns(outputRedirected);

        var exitCode = await Command().OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_INTERACTION_NEEDED);
        _fixture.Console.ErrorLines[0].ShouldBe("! sform cannot ask for the edit of the project settings: the input or the output is redirected.");
        _fixture.Console.ErrorLines[1].ShouldContain("sform project settings set -p org1.proj1 -i <file> --replace");
    }

    // The editor gives back the opened document after the change, through the command's check, as EditorService does.
    private void EditTo(Action<JObject> change, Action<JObject>? onChecked = null)
    {
        SetupEditor((original, check) =>
        {
            var edited = (JObject)original.DeepClone();
            change(edited);
            var result = check!(edited);

            if (result.Document is null)
            {
                // the user does not re-open the editor
                return new JsonEditResult(null, ExitCodes.WARNING_ABORTED);
            }

            onChecked?.Invoke(result.Document);
            return new JsonEditResult(result.Document, ExitCodes.SUCCESS);
        });
    }

    private void SetupEditor(Func<JObject, Func<JObject, JsonEditCheck>?, JsonEditResult> edit)
    {
        _editor
            .Setup(service => service.EditJsonAsync(
                It.IsAny<IExtendedConsole>(),
                It.IsAny<EditorOptions>(),
                It.IsAny<JObject>(),
                "project-settings-org1.proj1",
                false,
                It.IsAny<bool>(),
                It.IsAny<Func<JObject, JsonEditCheck>?>()))
            .Returns<IExtendedConsole, EditorOptions, JObject, string, bool, bool, Func<JObject, JsonEditCheck>?>((_, _, original, _, _, _, check) =>
            {
                _opened = original;
                return Task.FromResult(edit(original, check));
            });
    }

    private string KeptFile()
    {
        return _fixture.FileSystem.AllFiles.ShouldHaveSingleItem();
    }

    private ProjectSettingsEditCommand Command()
    {
        return new ProjectSettingsEditCommand(
            _fixture.Console.Object,
            _fixture.Context.Object,
            _fixture.Access.Object,
            _editor.Object,
            Options.Create(new EditorOptions()),
            _fixture.FileSystem,
            _fixture.Cache.Object);
    }
}
