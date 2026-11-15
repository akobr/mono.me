using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Commands.Projects;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services.Projects;
using _42.Platform.Cli.UnitTests.Output;
using _42.Platform.Storyteller;
using Alba.CsConsoleFormat;
using McMaster.Extensions.CommandLineUtils;
using Moq;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;
using SdkAppearance = _42.Platform.Storyteller.Sdk.ProjectTypeAppearance;

namespace _42.Platform.Cli.UnitTests.Commands.Projects;

// sform project settings: the table for people, the JSON of the API, the file shape of -e, and the refusals.
public class ProjectSettingsCommandTests
{
    private const string ExportPath = "/work/settings.json";

    private readonly ProjectSettingsFixture _fixture = new();
    private readonly Dictionary<string, string?> _environment = new(StringComparer.Ordinal);

    [Fact]
    public async Task Text_ListsTheSevenTypes_AndTheOtherSettings()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());

        var exitCode = await Command().OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fixture.Lines[0].ShouldBe($"# Project settings of {ProjectSettingsFixture.ProjectKey}");

        var table = _fixture.Console.Tables.ShouldHaveSingleItem();
        table.Headers.ShouldBe(["Code", "Type", "Name", "Plural", "Color", "Icon"]);
        table.Rows.Select(row => row[0]).ShouldBe(["rst", "unt", "sbt", "usg", "cnt", "exe", "uxe"]);
        table.Rows[0].ShouldBe(["rst", "Responsibility", "—", "—", "violet", "i-lucide-target"]);
        table.Rows[1].ShouldBe(["unt", "Unit", "—", "—", "teal", "i-lucide-box"]);
        table.Rows[2].ShouldBe(["sbt", "Subject", "Company", "Companies", "pink", "i-lucide-building-2"]);
        table.Rows[6].ShouldBe(["uxe", "Unit of execution", "—", "—", "orange", "i-lucide-cpu"]);

        _fixture.Lines.ShouldContain("Default time zone: Europe/Prague");
        _fixture.Lines.ShouldContain("Date format: dd.mm.yyyy");
        _fixture.Lines.ShouldContain("Version: 3");
        _fixture.Lines.ShouldContain($"Modified: {AuditConsoleExtensions.FormatTime(ProjectSettingsFixture.ModifiedAt)} by Ales Kobr");
    }

    [Fact]
    public async Task Text_OfAProjectWithoutSettings_ShowsTheDefaults()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Defaults());

        var exitCode = await Command().OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fixture.Console.Tables.ShouldHaveSingleItem().Rows[2].ShouldBe(["sbt", "Subject", "—", "—", "blue", "i-lucide-sun"]);
        _fixture.Lines.ShouldContain("Default time zone: UTC (default)");
        _fixture.Lines.ShouldContain("Date format: yyyy-mm-dd (default)");
        _fixture.Lines.ShouldContain("Version: 0");
        _fixture.Lines.ShouldContain("Modified: never");
    }

    [Fact]
    public async Task Text_OfALabelWithoutAPlural_ShowsTheDerivedPlural()
    {
        var settings = ProjectSettingsFixture.Defaults();
        settings.Types["exe"] = new SdkAppearance { Label = "Policy" };
        _fixture.SetupRead(settings);

        await Command().OnExecuteAsync();

        _fixture.Console.Tables.ShouldHaveSingleItem().Rows[5].Take(4).ShouldBe(["exe", "Execution", "Policy", "Policies"]);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("JSON")]
    public async Task Json_WritesTheSettingsAsTheApiAnswersThem(string format)
    {
        _fixture.Console.UseInnerConsole(redirectedInput: false);
        _fixture.SetupRead(ProjectSettingsFixture.Changed());

        var exitCode = await Command(format: format).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fixture.Lines.ShouldBeEmpty();
        _fixture.Console.Tables.ShouldBeEmpty();

        var json = JObject.Parse(_fixture.Console.OutputText);
        json["Types"]!["sbt"]!["Label"]!.Value<string>().ShouldBe("Company");
        json["DefaultTimeZone"]!.Value<string>().ShouldBe("Europe/Prague");
        json["Version"]!.Value<long>().ShouldBe(3);
        json["Modified"]!["By"]!["Name"]!.Value<string>().ShouldBe("Ales Kobr");
    }

    [Fact]
    public async Task Export_WritesTheFileShape()
    {
        _fixture.FileSystem.AddDirectory("/work");
        _fixture.SetupRead(ProjectSettingsFixture.Changed());

        var exitCode = await Command(export: ExportPath).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        var file = JObject.Parse(_fixture.FileSystem.File.ReadAllText(ExportPath));
        file["Format"]!.Value<string>().ShouldBe(_42.Platform.Storyteller.ProjectSettings.FileFormat);
        file["Types"]!["sbt"]!["Icon"]!.Value<string>().ShouldBe("i-lucide-building-2");
        file["DateFormat"]!.Value<string>().ShouldBe("dd.mm.yyyy");
        file.ContainsKey("Version").ShouldBeFalse();
        file.ContainsKey("Modified").ShouldBeFalse();

        // what set -i reads back
        ProjectSettingsJson.Read(System.Text.Json.Nodes.JsonNode.Parse(file.ToString())).IsValid.ShouldBeTrue();
    }

    [Fact]
    public async Task UnknownFormat_IsRefused_BeforeAnyRequest()
    {
        var exitCode = await Command(format: "yaml").OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Lines.ShouldBe(["! Unknown format 'yaml'; use text or json."]);
    }

    [Fact]
    public async Task Refusal_PrintsTheServerMessage()
    {
        _fixture.SetupRead(AnnotationCommandFixture.MissingScope("Annotation.Read"));

        var exitCode = await Command().OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        _fixture.Lines.ShouldBe([$"! {AnnotationCommandFixture.MissingScopeMessage("Annotation.Read")}", "Error code: AccessDenied"]);
    }

    [Fact]
    public async Task UnknownProject_IsWrongInput()
    {
        _fixture.SetupRead(AnnotationCommandFixture.Error(404, "NotFound", "The project doesn't exist."));

        var exitCode = await Command().OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Lines[0].ShouldBe("! The project doesn't exist.");
    }

    [Fact]
    public void Text_WithoutAConsole_IsPlainLines()
    {
        var output = new System.IO.StringWriter();
        var inner = new Mock<IConsole>();
        inner.SetupGet(console => console.Out).Returns(output);
        var console = new SformConsole(inner.Object, () => null, _ => null);

        console.WriteProjectSettings(
            ProjectSettingsFixture.ProjectKey,
            ProjectSettingsFixture.Changed(),
            console.ShowsTypeColors(_ => null),
            console.ShowsTypeIcons(Switches(OutputOptions.ICONS_ALWAYS)));

        var lines = output.ToString().Split(output.NewLine);
        lines.ShouldBe([
            "# Project settings of org1.proj1",
            "  Code  Type               Name     Plural     Color    Icon",
            " " + new string('─', 75),
            "  rst   Responsibility     —        —          violet   i-lucide-target",
            "  unt   Unit               —        —          teal     i-lucide-box",
            "  sbt   Subject            Company  Companies  pink     i-lucide-building-2",
            "  usg   Usage              —        —          amber    i-lucide-link",
            "  cnt   Context            —        —          cyan     i-lucide-layers",
            "  exe   Execution          —        —          emerald  i-lucide-play",
            "  uxe   Unit of execution  —        —          orange   i-lucide-cpu",
            "  Default time zone: Europe/Prague",
            "  Date format: dd.mm.yyyy",
            "  Version: 3",
            $"  Modified: {AuditConsoleExtensions.FormatTime(ProjectSettingsFixture.ModifiedAt)} by Ales Kobr",
            "  Change them with sform project settings set, edit and reset.",
            string.Empty,
        ]);
    }

    [Fact]
    public void TypeCells_WithColors_ShowASwatchInTheTypeColor()
    {
        var theme = _fixture.Console.Object.Theme;
        var builtIn = ProjectSettingsCatalog.Default.BuiltIn["sbt"];

        var cells = ProjectSettingsConsoleExtensions.TypeCells("sbt", new SdkAppearance { Color = "pink" }, builtIn, theme, showColors: true, showIcons: false).ToList();

        var spans = cells[4].Children.OfType<Span>().ToList();
        spans.Select(span => span.Text).ShouldBe(["■", " ", "pink"]);
        spans[0].Color.ShouldBe(ConsoleColor.Magenta);

        // the built-in color, dimmed after its swatch
        var builtInCells = ProjectSettingsConsoleExtensions.TypeCells("sbt", null, builtIn, theme, showColors: true, showIcons: false).ToList();
        var builtInSpans = builtInCells[4].Children.OfType<Span>().ToList();
        builtInSpans[0].Color.ShouldBe(ConsoleColor.Blue);
        builtInSpans[2].Color.ShouldBe(theme.LowlightColor);
    }

    [Fact]
    public void TypeCells_WithoutColors_ShowTheNameAlone()
    {
        var cells = ProjectSettingsConsoleExtensions.TypeCells("sbt", new SdkAppearance { Color = "pink" }, ProjectSettingsCatalog.Default.BuiltIn["sbt"], _fixture.Console.Object.Theme, showColors: false, showIcons: false).ToList();

        RecordingConsole.ToText(cells[4]).ShouldBe("pink");
    }

    // Spec 7.1: the icon's name and, where icons are shown, its symbol before it.
    [Fact]
    public void TypeCells_WithIcons_ShowTheSymbolBeforeTheName()
    {
        var theme = _fixture.Console.Object.Theme;
        var builtIn = ProjectSettingsCatalog.Default.BuiltIn["sbt"];

        var spans = ProjectSettingsConsoleExtensions.TypeCells("sbt", new SdkAppearance { Icon = "i-lucide-building-2" }, builtIn, theme, showColors: false, showIcons: true)
            .ToList()[5].Children.OfType<Span>().ToList();
        spans.Select(span => span.Text).ShouldBe(["\U0001F3E2 ", "i-lucide-building-2"]);

        // the built-in icon, dimmed after its symbol
        var builtInSpans = ProjectSettingsConsoleExtensions.TypeCells("sbt", null, builtIn, theme, showColors: false, showIcons: true)
            .ToList()[5].Children.OfType<Span>().ToList();
        builtInSpans.Select(span => span.Text).ShouldBe(["\u2600\uFE0F ", "i-lucide-sun"]);
        builtInSpans[1].Color.ShouldBe(theme.LowlightColor);
    }

    [Fact]
    public void TypeCells_WithoutIcons_ShowTheIconNameAlone()
    {
        var cells = ProjectSettingsConsoleExtensions.TypeCells("sbt", new SdkAppearance { Icon = "i-lucide-building-2" }, ProjectSettingsCatalog.Default.BuiltIn["sbt"], _fixture.Console.Object.Theme, showColors: false, showIcons: false).ToList();

        RecordingConsole.ToText(cells[5]).ShouldBe("i-lucide-building-2");
    }

    // A redirected output (sform project settings > settings.txt from a terminal, whose input and error keep the width of
    // the console) shows no swatches.
    [Theory]
    [InlineData(true, false, null, true)]
    [InlineData(true, false, "", true)]
    [InlineData(true, false, "1", false)]
    [InlineData(true, true, null, false)]
    [InlineData(false, false, null, false)]
    public void TypeColors_AreShownOnAConsoleWithoutNoColor(bool hasConsole, bool redirected, string? noColor, bool shown)
    {
        var console = new SformConsole(Mock.Of<IConsole>(inner => inner.IsOutputRedirected == redirected), () => hasConsole ? 120 : null, _ => null);
        var variables = new Dictionary<string, string?> { [SformVariables.NO_COLOR] = noColor };

        console.ShowsTypeColors(name => variables.GetValueOrDefault(name)).ShouldBe(shown);
    }

    [Fact]
    public void TypeColors_OfAnotherConsole_AreNotShown()
    {
        _fixture.Console.Object.ShowsTypeColors(_ => null).ShouldBeFalse();
    }

    // The symbols follow the icon switches as the console output of the other commands does; never without a console or
    // with the output redirected, even with always.
    [Theory]
    [InlineData(true, false, null, true, true, true)]
    [InlineData(true, false, null, false, true, false)]
    [InlineData(true, false, null, true, false, false)]
    [InlineData(true, false, "always", false, false, true)]
    [InlineData(true, false, "never", true, true, false)]
    [InlineData(true, true, "always", true, true, false)]
    [InlineData(false, false, "always", true, true, false)]
    public void TypeIcons_FollowTheSwitches_OnAConsole(bool hasConsole, bool redirected, string? icons, bool windowsTerminal, bool isUtf8, bool shown)
    {
        _environment[SformVariables.ICONS] = icons;
        _environment["WT_SESSION"] = windowsTerminal ? "1" : null;
        var inner = new ColorRecordingConsole(isUtf8 ? new UTF8Encoding(false) : Encoding.Latin1) { IsOutputRedirected = redirected };
        var console = new SformConsole(inner, () => hasConsole ? 120 : null, _ => null);

        console.ShowsTypeIcons(Switches()).ShouldBe(shown);
    }

    [Fact]
    public void TypeIcons_OfAnotherConsole_AreNotShown()
    {
        _fixture.Console.Object.ShowsTypeIcons(Switches(OutputOptions.ICONS_ALWAYS)).ShouldBeFalse();
    }

    private ProjectSettingsCommand Command(string? format = null, string? export = null)
    {
        return new ProjectSettingsCommand(_fixture.Console.Object, _fixture.Context.Object, _fixture.Access.Object, _fixture.FileSystem, Switches())
        {
            Format = format,
            ExportFilePath = export,
        };
    }

    // The switches of the run over the test's environment (and output.icons when given).
    private OutputSwitches Switches(string? configuredIcons = null)
    {
        return new OutputSwitches(new OutputOptions { Icons = configuredIcons }, name => _environment.GetValueOrDefault(name));
    }
}
