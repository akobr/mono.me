using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Cli.Authentication;
using _42.Platform.Cli.Commands.Account.Settings;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Services;
using _42.Platform.Cli.UnitTests.Configuration;
using _42.Platform.Storyteller.Sdk.Credentials;
using McMaster.Extensions.CommandLineUtils;
using Moq;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

// sform account settings: the list with where each value comes from, and editor, set and unset, which write
// ~/.42for.net/sform.settings.json only. No command needs a sign-in or the API.
public class AccountSettingsCommandsTests
{
    private static readonly string SettingsPath = UserSettingsStoreTests.SettingsPath;

    private readonly MockFileSystem _fileSystem = new(new Dictionary<string, MockFileData>(), MockUnixSupport.Path(@"C:\work"));
    private readonly RecordingConsole _console = new();
    private readonly Mock<IConsole> _innerConsole = new();
    private readonly StringWriter _out = new();
    private readonly Mock<IEditorService> _editor = new(MockBehavior.Strict);
    private readonly Dictionary<string, string?> _environment = [];

    public AccountSettingsCommandsTests()
    {
        _innerConsole.SetupGet(console => console.Out).Returns(_out);
        _console.Mock.SetupGet(console => console.Console).Returns(_innerConsole.Object);
        _editor.Setup(editor => editor.IsEditorAvailable(It.IsAny<EditorOptions>())).Returns(true);
    }

    [Fact]
    public async Task TheList_WithoutAnything_ShowsTheDefaults()
    {
        var exitCode = await List();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldBe(
        [
            "# Settings",
            $"File {SettingsPath}",
            "Change them with sform account settings editor, set and unset.",
        ]);
        var table = _console.Tables.ShouldHaveSingleItem();
        table.Headers.ShouldBe(["Key", "Value", "Source"]);
        table.Rows.ShouldBe(
        [
            ["editor.type", "(ask on first use)", "default"],
            ["editor.command", "(not set)", "default"],
            ["access.project", "house.main", "default"],
            ["access.view", "default", "default"],
        ]);
    }

    [Fact]
    public async Task TheList_ShowsTheValuesOfTheSettingsFile()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("""
            {
              "editor": { "editorType": "Custom", "customCommand": "code --wait" },
              "access": { "organizationName": "contoso", "projectName": "payments", "viewName": "preview" }
            }
            """));

        await List();

        _console.Tables.ShouldHaveSingleItem().Rows.ShouldBe(
        [
            ["editor.type", "custom", "settings"],
            ["editor.command", "code --wait", "settings"],
            ["access.project", "contoso.payments", "settings"],
            ["access.view", "preview", "settings"],
        ]);
    }

    [Fact]
    public async Task TheList_ShowsTheValuesOfTheLegacyFiles()
    {
        _fileSystem.AddFile(UserSettingsStoreTests.LegacyEditorPath, new MockFileData(UserSettingsStoreTests.LegacyEditorJson));
        _fileSystem.AddFile(UserSettingsStoreTests.LegacyAccessPath, new MockFileData(UserSettingsStoreTests.LegacyAccessJson));

        await List();

        _console.Tables.ShouldHaveSingleItem().Rows.ShouldBe(
        [
            ["editor.type", "neovim", "legacy editor.config.json"],
            ["editor.command", "(not set)", "default"],
            ["access.project", "contoso.payments", "legacy access.default.json"],
            ["access.view", "default", "default"],
        ]);
    }

    // The state that unset leaves while an old file cannot be deleted: the old editor is no longer used, nor listed.
    [Fact]
    public async Task TheList_DoesNotShowALegacyFileThatTheSettingsFileShadows()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"editor":{}}"""));
        _fileSystem.AddFile(UserSettingsStoreTests.LegacyEditorPath, new MockFileData(UserSettingsStoreTests.LegacyEditorJson));

        await List();

        _console.Tables.ShouldHaveSingleItem().Rows[0].ShouldBe(["editor.type", "(ask on first use)", "default"]);
    }

    [Theory]
    [InlineData("VISUAL", "$VISUAL")]
    [InlineData("EDITOR", "$EDITOR")]
    public async Task TheList_ShowsTheEditorOfTheEnvironment_WhenNoneIsConfigured(string variable, string source)
    {
        _environment[variable] = "nano -w";

        await List();

        var rows = _console.Tables.ShouldHaveSingleItem().Rows;
        rows[0].ShouldBe(["editor.type", "custom", source]);
        rows[1].ShouldBe(["editor.command", "nano -w", source]);
    }

    [Fact]
    public async Task TheList_ShowsTheConfiguredEditor_OverTheEnvironment()
    {
        _environment["EDITOR"] = "nano -w";
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"editor":{"editorType":"Vim","customCommand":"subl -w"}}"""));

        await List();

        var rows = _console.Tables.ShouldHaveSingleItem().Rows;
        rows[0].ShouldBe(["editor.type", "vim", "settings"]);
        rows[1].ShouldBe(["editor.command", "subl -w", "settings (used by a custom editor only)"]);
    }

    // CommandContext puts STORYTELLER_PROJECT and STORYTELLER_VIEW before the settings; the view of the settings belongs
    // to their project.
    [Fact]
    public async Task TheList_ShowsTheProjectOfTheEnvironment()
    {
        _environment[TargetVariables.PROJECT] = "acme.web";
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"access":{"organizationName":"contoso","projectName":"payments","viewName":"preview"}}"""));

        await List();

        var rows = _console.Tables.ShouldHaveSingleItem().Rows;
        rows[2].ShouldBe(["access.project", "acme.web", "STORYTELLER_PROJECT"]);
        rows[3].ShouldBe(["access.view", "default", "default (STORYTELLER_PROJECT is set)"]);
    }

    [Fact]
    public async Task TheList_ShowsTheViewOfTheEnvironment()
    {
        _environment[TargetVariables.VIEW] = "staging";

        await List();

        _console.Tables.ShouldHaveSingleItem().Rows[3].ShouldBe(["access.view", "staging", "STORYTELLER_VIEW"]);
    }

    [Fact]
    public async Task TheList_SaysThatAMachineSignInUsesNoDefaultProject()
    {
        _environment[StorytellerVariables.API_KEY] = "sk-test";
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"access":{"organizationName":"contoso","projectName":"payments"}}"""));

        await List();

        var rows = _console.Tables.ShouldHaveSingleItem().Rows;
        rows[2].ShouldBe(["access.project", "(of the machine sign-in)", "machine sign-in (STORYTELLER_API_KEY)"]);
        rows[3].ShouldBe(["access.view", "(of the machine sign-in)", "machine sign-in (STORYTELLER_API_KEY)"]);
    }

    [Fact]
    public async Task TheList_ShowsAProjectWithoutAnOrganization_AsTheCommandsUseIt()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"access":{"projectName":"payments"}}"""));

        await List();

        _console.Tables.ShouldHaveSingleItem().Rows[2].ShouldBe(["access.project", "house.payments", "settings"]);
    }

    [Fact]
    public async Task Path_PrintsOnlyTheFile()
    {
        var exitCode = await List(showPath: true);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _out.ToString().ShouldBe(SettingsPath + Environment.NewLine);
        _console.Lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task Path_WorksWithADamagedFile()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("{"));

        (await List(showPath: true)).ShouldBe(ExitCodes.SUCCESS);
        _out.ToString().ShouldBe(SettingsPath + Environment.NewLine);
    }

    [Fact]
    public async Task Json_WritesTheValuesThatSformUses()
    {
        _environment["VISUAL"] = "code --wait";
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"access":{"organizationName":"contoso","projectName":"payments"}}"""));

        (await List(json: true)).ShouldBe(ExitCodes.SUCCESS);

        JObject.Parse(_out.ToString()).ShouldBeJson("""
            {
              "editor.type": "custom",
              "editor.command": "code --wait",
              "access.project": "contoso.payments",
              "access.view": "default"
            }
            """);
        _console.Lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheList_OfADamagedFile_IsWrongInput()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("[]"));

        var exitCode = await List();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.Lines.ShouldBe([$"! The settings file {SettingsPath} is damaged: it is no JSON object."]);
    }

    [Theory]
    [InlineData("vscode", null, """{"editorType":"VsCode"}""", "Editor set to vscode.")]
    [InlineData("NEOVIM", null, """{"editorType":"Neovim"}""", "Editor set to neovim.")]
    [InlineData("vim", null, """{"editorType":"Vim"}""", "Editor set to vim.")]
    [InlineData("custom", " code --wait ", """{"editorType":"Custom","customCommand":"code --wait"}""", "Editor set to custom (code --wait).")]
    [InlineData(null, "subl -w", """{"editorType":"Custom","customCommand":"subl -w"}""", "Editor set to custom (subl -w).")]
    public async Task Editor_SavesTheChoice_WithoutAPrompt(string? type, string? command, string expected, string line)
    {
        var exitCode = await Editor(type, command);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        Stored()[ConfigurationSections.EDITOR].ShouldBeJson(expected);
        _console.Lines.ShouldBe([line]);
    }

    [Fact]
    public async Task Editor_OfAnotherType_RemovesTheCustomCommand()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"editor":{"editorType":"Custom","customCommand":"subl -w"}}"""));

        (await Editor("vim", null)).ShouldBe(ExitCodes.SUCCESS);

        Stored()[ConfigurationSections.EDITOR].ShouldBeJson("""{"editorType":"Vim"}""");
    }

    [Theory]
    [InlineData("custom", null, "! A custom editor needs its command line: sform account settings editor custom --command \"code --wait\".")]
    [InlineData("custom", " ", "! A custom editor needs its command line: sform account settings editor custom --command \"code --wait\".")]
    [InlineData("vim", "vim -n", "! --command is for a custom editor only; vim needs none.")]
    [InlineData("notepad", null, "! 'notepad' is no editor sform knows; use vscode, neovim, vim, custom.")]
    public async Task Editor_RefusesAWrongChoice(string type, string? command, string line)
    {
        var exitCode = await Editor(type, command);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.Lines.ShouldBe([line]);
        _fileSystem.File.Exists(SettingsPath).ShouldBeFalse();
    }

    [Fact]
    public async Task Editor_WithoutAType_Asks()
    {
        _editor.Setup(editor => editor.PromptEditorPreference(It.IsAny<_42.CLI.Toolkit.Output.IExtendedConsole>()))
            .Returns(new EditorOptions { EditorType = EditorType.Neovim });

        (await Editor(null, null)).ShouldBe(ExitCodes.SUCCESS);

        Stored()[ConfigurationSections.EDITOR].ShouldBeJson("""{"editorType":"Neovim"}""");
        _console.Lines.ShouldBe(["Editor set to neovim."]);
    }

    [Fact]
    public async Task Editor_WithoutATypeAndWithRedirectedInput_DoesNotAsk()
    {
        _innerConsole.SetupGet(console => console.IsInputRedirected).Returns(true);

        var exitCode = await Editor(null, null);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.Lines.ShouldBe(["Name the editor: sform account settings editor <vscode|neovim|vim|custom> [--command \"<command>\"]."]);
        _editor.Verify(editor => editor.PromptEditorPreference(It.IsAny<_42.CLI.Toolkit.Output.IExtendedConsole>()), Times.Never);
    }

    [Fact]
    public async Task Editor_WithACustomChoiceButNoCommand_SavesNothing()
    {
        _editor.Setup(editor => editor.PromptEditorPreference(It.IsAny<_42.CLI.Toolkit.Output.IExtendedConsole>()))
            .Returns(new EditorOptions { EditorType = EditorType.Custom });

        (await Editor(null, null)).ShouldBe(ExitCodes.WARNING_ABORTED);

        _fileSystem.File.Exists(SettingsPath).ShouldBeFalse();
    }

    [Fact]
    public async Task Editor_WithTheStoredChoice_WritesNothing()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"editor":{"editorType":"Vim"}}"""));
        var written = _fileSystem.File.GetLastWriteTimeUtc(SettingsPath);

        (await Editor("vim", null)).ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);

        _console.Lines.ShouldBe(["The editor is already vim."]);
        _fileSystem.File.GetLastWriteTimeUtc(SettingsPath).ShouldBe(written);
    }

    // Only the legacy file has it: saving it moves it into the settings file.
    [Fact]
    public async Task Editor_WithTheChoiceOfALegacyFile_MovesIt()
    {
        _fileSystem.AddFile(UserSettingsStoreTests.LegacyEditorPath, new MockFileData(UserSettingsStoreTests.LegacyEditorJson));

        (await Editor("neovim", null)).ShouldBe(ExitCodes.SUCCESS);

        Stored()[ConfigurationSections.EDITOR].ShouldBeJson("""{"editorType":"Neovim"}""");
        _fileSystem.File.Exists(UserSettingsStoreTests.LegacyEditorPath).ShouldBeFalse();
    }

    [Fact]
    public async Task Editor_ThatIsNotInstalled_IsSavedWithAWarning()
    {
        _editor.Setup(editor => editor.IsEditorAvailable(It.IsAny<EditorOptions>())).Returns(false);

        (await Editor("custom", "subl -w")).ShouldBe(ExitCodes.SUCCESS);

        _console.Lines.ShouldBe(["Editor set to custom (subl -w).", "! custom (subl -w) was not found on this machine; the choice is saved anyway."]);
        Stored()[ConfigurationSections.EDITOR].ShouldBeJson("""{"editorType":"Custom","customCommand":"subl -w"}""");
    }

    [Fact]
    public async Task Editor_WithADamagedFile_IsWrongInput_AndWritesNothing()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("{"));

        (await Editor("vim", null)).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);

        _console.Lines.ShouldHaveSingleItem().ShouldStartWith($"! The editor could not be saved: The settings file {SettingsPath} is damaged: ");
        _fileSystem.File.ReadAllText(SettingsPath).ShouldBe("{");
    }

    [Theory]
    [InlineData("editor.type", "VSCode", "editor", """{"editorType":"VsCode"}""", "editor.type = vscode")]
    [InlineData("editor.command", " nano -w ", "editor", """{"customCommand":"nano -w"}""", "editor.command = nano -w")]
    [InlineData("access.project", "contoso.payments", "access", """{"organizationName":"contoso","projectName":"payments"}""", "access.project = contoso.payments")]
    [InlineData("ACCESS.VIEW", "preview", "access", """{"viewName":"preview"}""", "access.view = preview")]
    public async Task Set_WritesTheKey(string key, string value, string section, string expected, string line)
    {
        var exitCode = await Set(key, value);

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        Stored()[section].ShouldBeJson(expected);
        _console.Lines[0].ShouldBe(line);
    }

    [Fact]
    public async Task Set_TheTypeOfAnotherEditor_KeepsTheCommand()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"editor":{"editorType":"Custom","customCommand":"subl -w"}}"""));

        (await Set("editor.type", "vim")).ShouldBe(ExitCodes.SUCCESS);

        Stored()[ConfigurationSections.EDITOR].ShouldBeJson("""{"editorType":"Vim","customCommand":"subl -w"}""");
    }

    [Fact]
    public async Task Set_ACommandWithoutACustomType_SaysWhenItIsUsed()
    {
        (await Set("editor.command", "subl -w")).ShouldBe(ExitCodes.SUCCESS);

        _console.Lines.ShouldBe(["editor.command = subl -w", "sform uses it when editor.type is custom: sform account settings set editor.type custom."]);
    }

    [Fact]
    public async Task Set_ACustomTypeWithoutACommand_AsksForTheCommand()
    {
        (await Set("editor.type", "custom")).ShouldBe(ExitCodes.SUCCESS);

        _console.Lines.ShouldBe(["editor.type = custom", "A custom editor needs its command too: sform account settings set editor.command \"code --wait\"."]);
    }

    [Fact]
    public async Task Set_TheProject_KeepsTheView()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"access":{"organizationName":"contoso","projectName":"payments","viewName":"preview"}}"""));

        (await Set("access.project", "acme.web")).ShouldBe(ExitCodes.SUCCESS);

        Stored()[ConfigurationSections.ACCESS].ShouldBeJson("""{"organizationName":"acme","projectName":"web","viewName":"preview"}""");
    }

    [Theory]
    [InlineData("editor.colour", "red", "! 'editor.colour' is no setting sform knows; the settings are editor.type, editor.command, access.project, access.view.")]
    [InlineData(null, null, "! Name the setting; the settings are editor.type, editor.command, access.project, access.view.")]
    [InlineData("editor.type", "notepad", "! 'notepad' is no value of editor.type; the values are vscode, neovim, vim, custom.")]
    [InlineData("editor.type", null, "! Give the value of editor.type; the values are vscode, neovim, vim, custom.")]
    [InlineData("access.project", "payments", "! 'payments' is no value of access.project; it takes <organization>.<project>.")]
    [InlineData("access.project", ".payments", "! '.payments' is no value of access.project; it takes <organization>.<project>.")]
    [InlineData("access.project", "contoso.", "! 'contoso.' is no value of access.project; it takes <organization>.<project>.")]
    [InlineData("access.view", " ", "! ' ' is no value of access.view; it takes a view name.")]
    public async Task Set_RefusesAnUnknownKeyOrAnInvalidValue(string? key, string? value, string line)
    {
        var exitCode = await Set(key, value);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.Lines.ShouldBe([line]);
        _fileSystem.File.Exists(SettingsPath).ShouldBeFalse();
    }

    [Fact]
    public async Task Set_TheStoredValue_WritesNothing()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"access":{"viewName":"preview"}}"""));

        (await Set("access.view", "preview")).ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);

        _console.Lines.ShouldBe(["access.view is already preview."]);
    }

    [Fact]
    public async Task Unset_RemovesTheKey()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"editor":{"editorType":"Vim"},"access":{"viewName":"preview"}}"""));

        (await Unset("editor.type")).ShouldBe(ExitCodes.SUCCESS);

        Stored().ShouldBeJson("""{"access":{"viewName":"preview"}}""");
        _console.Lines.ShouldBe(["editor.type is unset; the next edit uses $VISUAL or $EDITOR, or asks."]);
    }

    [Fact]
    public async Task Unset_TheProject_KeepsTheView()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"access":{"organizationName":"contoso","projectName":"payments","viewName":"preview"}}"""));

        (await Unset("access.project")).ShouldBe(ExitCodes.SUCCESS);

        Stored()[ConfigurationSections.ACCESS].ShouldBeJson("""{"viewName":"preview"}""");
        _console.Lines.ShouldBe(["access.project is unset; sform uses house.main."]);
    }

    // Unset moves the legacy sections first, so the old value does not show through again.
    [Fact]
    public async Task Unset_OfAValueOfALegacyFile_HidesIt()
    {
        _fileSystem.AddFile(UserSettingsStoreTests.LegacyAccessPath, new MockFileData(UserSettingsStoreTests.LegacyAccessJson));

        (await Unset("access.project")).ShouldBe(ExitCodes.SUCCESS);

        _fileSystem.File.Exists(UserSettingsStoreTests.LegacyAccessPath).ShouldBeFalse();
        Stored().ContainsKey(ConfigurationSections.ACCESS).ShouldBeFalse();
    }

    // A value that sform cannot use is still in the file, and unset removes it.
    [Fact]
    public async Task Unset_RemovesAnInvalidValue()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"editor":{"editorType":"notepad"}}"""));

        (await Unset("editor.type")).ShouldBe(ExitCodes.SUCCESS);

        Stored().ContainsKey(ConfigurationSections.EDITOR).ShouldBeFalse();
    }

    [Fact]
    public async Task Unset_OfAKeyThatIsNotSet_WritesNothing()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("""{"editor":{}}"""));
        _fileSystem.AddFile(UserSettingsStoreTests.LegacyEditorPath, new MockFileData(UserSettingsStoreTests.LegacyEditorJson));

        (await Unset("editor.type")).ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);

        _console.Lines.ShouldBe(["editor.type is not set."]);
        _fileSystem.File.Exists(UserSettingsStoreTests.LegacyEditorPath).ShouldBeTrue();
    }

    [Fact]
    public async Task Unset_OfAnUnknownKey_IsWrongInput()
    {
        (await Unset("editor.colour")).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
    }

    [Fact]
    public void TheHelpText_NamesEveryKey()
    {
        foreach (var definition in UserSettingDefinitions.All)
        {
            AccountSettingsCommand.HELP_TEXT.ShouldContain($"  {definition.Key} ");
        }

        UserSettingDefinitions.All.Select(definition => definition.Key).ShouldBe(["editor.type", "editor.command", "access.project", "access.view"]);
    }

    private UserSettingsStore CreateStore()
    {
        return new UserSettingsStore(_fileSystem, SettingsPath, UserSettingsStoreTests.LegacyPaths());
    }

    private Task<int> List(bool showPath = false, bool json = false)
    {
        return new AccountSettingsCommand(
            _console.Object,
            CreateStore(),
            EnvironmentEditor.FromEnvironment(name => _environment.GetValueOrDefault(name)),
            MachineSignInSettings.FromEnvironment(name => _environment.GetValueOrDefault(name)))
        {
            ShowPath = showPath,
            Json = json,
        }.OnExecuteAsync();
    }

    private Task<int> Editor(string? type, string? command)
    {
        return new AccountSettingsEditorCommand(_console.Object, _editor.Object, CreateStore())
        {
            Type = type,
            EditorCommand = command,
        }.OnExecuteAsync();
    }

    private Task<int> Set(string? key, string? value)
    {
        return new AccountSettingsSetCommand(_console.Object, CreateStore()) { Key = key, Value = value }.OnExecuteAsync();
    }

    private Task<int> Unset(string? key)
    {
        return new AccountSettingsUnsetCommand(_console.Object, CreateStore()) { Key = key }.OnExecuteAsync();
    }

    private JObject Stored()
    {
        return JObject.Parse(_fileSystem.File.ReadAllText(SettingsPath));
    }
}
