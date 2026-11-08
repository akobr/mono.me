using System.Collections.Generic;
using System.ComponentModel;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Services;
using _42.Platform.Cli.UnitTests.Commands;
using _42.Platform.Cli.UnitTests.Configuration;
using Moq;
using Newtonsoft.Json.Linq;
using Sharprompt;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Services;

// Which editor an edit opens and what is saved: the configured one, $VISUAL / $EDITOR for the run only, or the picker,
// whose choice goes into sform.settings.json.
public class EditorServiceSettingsTests
{
    private static readonly string SettingsPath = UserSettingsStoreTests.SettingsPath;

    private readonly MockFileSystem _fileSystem = new(new Dictionary<string, MockFileData>(), MockUnixSupport.Path(@"C:\work"));
    private readonly RecordingConsole _console = new();

    [Fact]
    public async Task TheEnvironmentEditor_IsUsedForTheRun_AndNotSaved()
    {
        var service = CreateService(EnvironmentEditor.FromEnvironment(name => name == "EDITOR" ? "nano -w" : null));

        var result = await Edit(service, new EditorOptions());

        result.ExitCode.ShouldBe(ExitCodes.SUCCESS);
        service.Opened.ShouldHaveSingleItem().CustomCommand.ShouldBe("nano -w");
        service.Opened[0].EditorType.ShouldBe(EditorType.Custom);
        _console.Lines[0].ShouldBe("Using $EDITOR (nano -w); change it with sform account settings editor.");
        _fileSystem.File.Exists(SettingsPath).ShouldBeFalse();
    }

    [Fact]
    public async Task AConfiguredEditor_WinsOverTheEnvironment()
    {
        var service = CreateService(EnvironmentEditor.FromEnvironment(name => name == "VISUAL" ? "code --wait" : null));

        await Edit(service, new EditorOptions { EditorType = EditorType.Vim });

        service.Opened.ShouldHaveSingleItem().EditorType.ShouldBe(EditorType.Vim);
        _console.Lines.ShouldNotContain(line => line.StartsWith("Using $"));
    }

    [Fact]
    public async Task WithoutAnyEditor_ThePickerAsks_AndTheChoiceIsSaved()
    {
        _console.Mock.Setup(console => console.Select(It.IsAny<SelectOptions<string>>())).Returns("Neovim");
        var service = CreateService(EnvironmentEditor.None);

        var result = await Edit(service, new EditorOptions());

        result.ExitCode.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines[0].ShouldBe("! No editor is configured yet. Let's set one up.");
        _console.Lines[1].ShouldBe($"Saved in {SettingsPath}; change it with sform account settings editor.");
        service.Opened.ShouldHaveSingleItem().EditorType.ShouldBe(EditorType.Neovim);
        JObject.Parse(_fileSystem.File.ReadAllText(SettingsPath)).ShouldBeJson("""{"editor":{"editorType":"Neovim"}}""");
    }

    [Fact]
    public async Task ThePicker_OffersTheInstalledEditors()
    {
        SelectOptions<string>? offered = null;
        _console.Mock.Setup(console => console.Select(It.IsAny<SelectOptions<string>>()))
            .Callback<SelectOptions<string>>(options => offered = options)
            .Returns("Vim");
        var service = CreateService(EnvironmentEditor.None, onPath: ["code", "vim"]);

        await Edit(service, new EditorOptions());

        offered!.Items.ShouldBe(["Visual Studio Code", "Vim", "Custom command"]);
    }

    [Fact]
    public async Task ACustomCommand_IsAskedFor_WithAnExampleThatWaits()
    {
        InputOptions<string>? asked = null;
        _console.Mock.Setup(console => console.Select(It.IsAny<SelectOptions<string>>())).Returns("Custom command");
        _console.Mock.Setup(console => console.Input(It.IsAny<InputOptions<string>>()))
            .Callback<InputOptions<string>>(options => asked = options)
            .Returns(" code --wait ");
        var service = CreateService(EnvironmentEditor.None);

        await Edit(service, new EditorOptions());

        asked!.Message.ShouldEndWith(", e.g. code --wait");
        JObject.Parse(_fileSystem.File.ReadAllText(SettingsPath)).ShouldBeJson("""{"editor":{"editorType":"Custom","customCommand":"code --wait"}}""");
    }

    [Fact]
    public async Task ACustomEditorWithoutACommand_AbortsTheEdit()
    {
        _console.Mock.Setup(console => console.Select(It.IsAny<SelectOptions<string>>())).Returns("Custom command");
        _console.Mock.Setup(console => console.Input(It.IsAny<InputOptions<string>>())).Returns("  ");
        var service = CreateService(EnvironmentEditor.None);

        var result = await Edit(service, new EditorOptions());

        result.ExitCode.ShouldBe(ExitCodes.WARNING_ABORTED);
        service.Opened.ShouldBeEmpty();
        _fileSystem.File.Exists(SettingsPath).ShouldBeFalse();
    }

    [Fact]
    public async Task AChoiceThatCannotBeSaved_IsReported_AndTheEditGoesOn()
    {
        _fileSystem.AddFile(SettingsPath, new MockFileData("{"));
        _console.Mock.Setup(console => console.Select(It.IsAny<SelectOptions<string>>())).Returns("Vim");
        var service = CreateService(EnvironmentEditor.None);

        var result = await Edit(service, new EditorOptions());

        result.ExitCode.ShouldBe(ExitCodes.SUCCESS);
        service.Opened.ShouldHaveSingleItem().EditorType.ShouldBe(EditorType.Vim);
        _console.Lines.ShouldContain(line => line.StartsWith($"! Your choice could not be saved: The settings file {SettingsPath} is damaged: "));
        _fileSystem.File.ReadAllText(SettingsPath).ShouldBe("{");
    }

    [Fact]
    public async Task AConfiguredEditorThatCannotStart_IsReported_InsteadOfCrashing()
    {
        var service = CreateService(EnvironmentEditor.None);
        service.StartFailures.Enqueue(new Win32Exception("The system cannot find the file specified."));

        var result = await Edit(service, new EditorOptions { EditorType = EditorType.Custom, CustomCommand = @"""C:\Tools\ed.exe"" -w" });

        result.ExitCode.ShouldBe(ExitCodes.ERROR_CRASH);
        _console.Lines.ShouldBe(
        [
            @"! The editor could not be started (C:\Tools\ed.exe): The system cannot find the file specified.",
            "Choose another one with sform account settings editor.",
        ]);
    }

    // $VISUAL and $EDITOR come before asking, so a variable that names no editor ends in the picker, not in a failure.
    [Fact]
    public async Task AnEnvironmentEditorThatCannotStart_FallsBackToThePicker()
    {
        _console.Mock.Setup(console => console.Select(It.IsAny<SelectOptions<string>>())).Returns("Vim");
        var service = CreateService(EnvironmentEditor.FromEnvironment(name => name == "EDITOR" ? "subl -w" : null));
        service.StartFailures.Enqueue(new Win32Exception("The system cannot find the file specified."));

        var result = await Edit(service, new EditorOptions());

        result.ExitCode.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldContain("! The editor could not be started (subl): The system cannot find the file specified.");
        service.Opened.ShouldHaveSingleItem().EditorType.ShouldBe(EditorType.Vim);
        JObject.Parse(_fileSystem.File.ReadAllText(SettingsPath)).ShouldBeJson("""{"editor":{"editorType":"Vim"}}""");
    }

    [Theory]
    [InlineData("code --wait", "code", "--wait \"F\"")]
    [InlineData("  nano  ", "nano", "\"F\"")]
    [InlineData("vim\t-n", "vim", "-n \"F\"")]
    [InlineData("\"C:\\Program Files\\Notepad++\\notepad++.exe\" -multiInst -nosession", "C:\\Program Files\\Notepad++\\notepad++.exe", "-multiInst -nosession \"F\"")]
    [InlineData("'/Applications/Sublime Text.app/Contents/SharedSupport/bin/subl' -w", "/Applications/Sublime Text.app/Contents/SharedSupport/bin/subl", "-w \"F\"")]
    [InlineData("\"C:\\Tools\\edit.exe\"", "C:\\Tools\\edit.exe", "\"F\"")]
    [InlineData("\"unbalanced -x", "\"unbalanced", "-x \"F\"")]
    public void ACustomCommand_IsSplitIntoTheExecutableAndItsArguments(string command, string executable, string arguments)
    {
        EditorService.ParseCustomCommand(command, "F").ShouldBe((executable, arguments));
    }

    // VS Code ships code.cmd and an extensionless sh script; Windows starts the .cmd, which CreateProcess does not find alone.
    [Fact]
    public void OnWindows_ACommandName_IsFoundWithTheExtensionsOfPathext()
    {
        var bin = MockUnixSupport.Path(@"C:\Program Files\Microsoft VS Code\bin");
        _fileSystem.AddFile(MockUnixSupport.Path(@"C:\Program Files\Microsoft VS Code\bin\code"), new MockFileData(string.Empty));
        _fileSystem.AddFile(MockUnixSupport.Path(@"C:\Program Files\Microsoft VS Code\bin\code.cmd"), new MockFileData(string.Empty));

        EditorService.FindExecutable(_fileSystem, "code", $"{MockUnixSupport.Path(@"C:\Windows")}; \"{bin}\" ", ".COM;.EXE;.BAT;.CMD", isWindows: true)
            .ShouldBe(MockUnixSupport.Path(@"C:\Program Files\Microsoft VS Code\bin\code.cmd"));
    }

    [Fact]
    public void OnWindows_ANameWithAnExtension_IsFoundAsItIs()
    {
        _fileSystem.AddFile(MockUnixSupport.Path(@"C:\Tools\notepad++.exe"), new MockFileData(string.Empty));

        EditorService.FindExecutable(_fileSystem, "notepad++.exe", MockUnixSupport.Path(@"C:\Tools"), null, isWindows: true)
            .ShouldBe(MockUnixSupport.Path(@"C:\Tools\notepad++.exe"));
        EditorService.FindExecutable(_fileSystem, "notepad++", MockUnixSupport.Path(@"C:\Tools"), null, isWindows: true)
            .ShouldBe(MockUnixSupport.Path(@"C:\Tools\notepad++.exe"));
    }

    [Fact]
    public void APath_IsLookedForAsAFile_NotOnThePath()
    {
        var path = MockUnixSupport.Path(@"C:\Tools\edit.exe");

        EditorService.FindExecutable(_fileSystem, path, MockUnixSupport.Path(@"C:\Tools"), null, isWindows: true).ShouldBeNull();
        _fileSystem.AddFile(path, new MockFileData(string.Empty));
        EditorService.FindExecutable(_fileSystem, path, null, null, isWindows: true).ShouldBe(path);
    }

    [Fact]
    public void OnUnix_TheFoldersOfThePath_AreSplitAtColons()
    {
        Assert.SkipWhen(System.OperatingSystem.IsWindows(), "Unix paths have no drive letter to split the PATH at.");
        _fileSystem.AddFile("/usr/bin/nano", new MockFileData(string.Empty));

        EditorService.FindExecutable(_fileSystem, "nano", "/usr/local/bin:/usr/bin", null, isWindows: false).ShouldBe("/usr/bin/nano");
        EditorService.FindExecutable(_fileSystem, "nano.exe", "/usr/local/bin:/usr/bin", null, isWindows: false).ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ABlankCommand_IsNotFound(string executable)
    {
        EditorService.FindExecutable(_fileSystem, executable, MockUnixSupport.Path(@"C:\Tools"), null, isWindows: true).ShouldBeNull();
    }

    [Fact]
    public void AnEditorOnThePath_IsAvailable()
    {
        var service = CreateService(EnvironmentEditor.None, onPath: ["nvim", "nano"]);

        service.IsEditorAvailable(new EditorOptions { EditorType = EditorType.Neovim }).ShouldBeTrue();
        service.IsEditorAvailable(new EditorOptions { EditorType = EditorType.VsCode }).ShouldBeFalse();
        service.IsEditorAvailable(new EditorOptions { EditorType = EditorType.Custom, CustomCommand = "nano -w" }).ShouldBeTrue();
        service.IsEditorAvailable(new EditorOptions()).ShouldBeFalse();
    }

    private TestEditorService CreateService(EnvironmentEditor environment, IReadOnlyCollection<string>? onPath = null)
    {
        var store = new UserSettingsStore(_fileSystem, SettingsPath, UserSettingsStoreTests.LegacyPaths());
        return new TestEditorService(_fileSystem, store, environment, onPath ?? []);
    }

    private Task<JsonEditResult> Edit(EditorService service, EditorOptions options)
    {
        return service.EditJsonAsync(_console.Object, options, new JObject { ["a"] = 1 }, "config-test", isNew: false, confirm: false);
    }

    // Opens no process: records the editor, and edits the file as the user would.
    private sealed class TestEditorService : EditorService
    {
        private readonly MockFileSystem _fileSystem;
        private readonly IReadOnlyCollection<string> _onPath;

        public TestEditorService(MockFileSystem fileSystem, IUserSettingsStore settings, EnvironmentEditor environment, IReadOnlyCollection<string> onPath)
            : base(fileSystem, settings, environment)
        {
            _fileSystem = fileSystem;
            _onPath = onPath;
        }

        public List<EditorOptions> Opened { get; } = [];

        public Queue<Win32Exception> StartFailures { get; } = new();

        public override Task<int> OpenFileInEditorAsync(string filePath, EditorOptions options)
        {
            if (StartFailures.TryDequeue(out var failure))
            {
                throw failure;
            }

            Opened.Add(options);
            _fileSystem.File.WriteAllText(filePath, """{"a":2}""");
            return Task.FromResult(0);
        }

        protected override bool IsOnPath(string command)
        {
            return _onPath.Contains(command);
        }
    }
}
