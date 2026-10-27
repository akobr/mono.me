using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Commands.Configuration;
using _42.Platform.Cli.GitHubActions;
using _42.Platform.Cli.Output;
using _42.Platform.Storyteller.Sdk;
using Alba.CsConsoleFormat;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

// sform config composition: the steps of the composition as a table, the effective document with the step of each line,
// and the refusals of the API.
public class ConfigCompositionCommandTests
{
    private const string Key = "exe.northwind.invoicing.prod";

    private readonly AnnotationCommandFixture _fixture = new();
    private readonly Mock<IConfigurationsApiClient> _api = new(MockBehavior.Strict);

    [Fact]
    public async Task Composition_CallsTheSdkWithTheTargetAndTheKey()
    {
        SetupComposition(Execution());

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _api.Verify(api => api.GetConfigurationCompositionAsync("org1", "proj1", "default", Key, null), Times.Once);
        _api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Composition_ListsTheStepsInOrder_WithTheNodeIndentedByItsDepth()
    {
        SetupComposition(Execution());

        await Execute();

        _fixture.Console.Lines[0].ShouldBe($"# Composition of the configuration for '{Key}'");
        var table = _fixture.Console.Tables.ShouldHaveSingleItem();
        table.Headers.ShouldBe(["Step", "Applied at", "Source", "Version", "Presence", "Values"]);
        table.Rows.ShouldBe(
        [
            ["1", "    rst.invoicing", "configuration", "—", "missing", "0"],
            ["2", "    sbt.northwind", "configuration", "v3", "present", "1"],
            ["3", "  usg.northwind.invoicing", "configuration", "v2", "present", "1"],
            ["4", "    sbt.northwind", "configuration", "v3", "present", "1"],
            ["5", "  cnt.northwind.prod", "configuration", "v1", "present", "1"],
            ["6", Key, "template Execution", "v4", "present", "1"],
            ["7", Key, "configuration", "v1", "empty", "0"],
        ]);
    }

    [Fact]
    public void StepTable_In80Columns_WrapsOnlyTheKey()
    {
        var rows = CompositionConsoleExtensions.GetStepRows(Execution());

        var lines = RenderTable(rows, CompositionConsoleExtensions.StepColumns, 80);

        lines.ShouldContain(line => Regex.IsMatch(line, @"^\s*Step\s+Applied at\s+Source\s+Version\s+Presence\s+Values\s*$"));
        foreach (var row in rows)
        {
            var pattern = $@"^\s*{row[0]}\s+.*\s{Regex.Escape(row[2])}\s+{Regex.Escape(row[3])}\s+{row[4]}\s+{row[5]}\s*$";
            lines.ShouldContain(line => Regex.IsMatch(line, pattern), $"row {row[0]}:{Environment.NewLine}{string.Join(Environment.NewLine, lines)}");
        }
    }

    [Fact]
    public async Task Composition_WritesEachLineOfTheEffectiveDocumentWithItsStep()
    {
        SetupComposition(Execution());

        await Execute();

        var listing = _fixture.Console.Lines.SkipWhile(line => line != "# Effective configuration, by step").Skip(1);
        listing.ShouldBe(
        [
            "    │ {",
            "  4 │   \"currency\": \"NOK\",",
            "  2 │   \"residency\": \"norway\",",
            "  3 │   \"plan\": \"enterprise\",",
            "  5 │   \"endpoint\": \"https://prod.northwind.example\",",
            "  6 │   \"mode\": \"batch\"",
            "    │ }",
        ]);
    }

    [Fact]
    public async Task Composition_NotFound_SaysSoAndIsWrongInput()
    {
        _api.Setup(api => api.GetConfigurationCompositionAsync("org1", "proj1", "default", Key, null))
            .ThrowsAsync(AnnotationCommandFixture.Error(404, "AnnotationDeleted", "The annotation is deleted."));

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Console.Lines.ShouldBe([$"The configuration for '{Key}' has not been found."]);
        _fixture.Console.Tables.ShouldBeEmpty();
    }

    [Fact]
    public async Task Composition_OfARestrictedMachine_PrintsTheServersMessageAndCode()
    {
        const string message = "The machine is restricted to the annotation exe.northwind.invoicing.prod and cannot read the whole project.";
        _api.Setup(api => api.GetConfigurationCompositionAsync("org1", "proj1", "default", Key, null))
            .ThrowsAsync(AnnotationCommandFixture.Error(403, "AnnotationRestricted", message));

        var exitCode = await Execute();

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        _fixture.Console.Lines.ShouldBe([$"! {message}", "Error code: AnnotationRestricted"]);
    }

    [Fact]
    public async Task Composition_Unauthorized_IsLeftToProgram()
    {
        _api.Setup(api => api.GetConfigurationCompositionAsync("org1", "proj1", "default", Key, null))
            .ThrowsAsync(AnnotationCommandFixture.Error(401));

        await Should.ThrowAsync<ApiException>(() => Execute());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-key")]
    public async Task Composition_OfAnInvalidKey_IsWrongInputWithoutACall(string key)
    {
        var exitCode = await Execute(key);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Console.Lines[0].ShouldBe("! Please specify a valid annotation key.");
        _api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Composition_WithExport_WritesTheWholeResponseToTheFile()
    {
        SetupComposition(Execution());
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>(), MockUnixSupport.Path(@"C:\work"));

        var exitCode = await Execute(fileSystem: fileSystem, exportFilePath: "composition.json");

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        var exported = JObject.Parse(fileSystem.File.ReadAllText(MockUnixSupport.Path(@"C:\work\composition.json")));
        exported["AnnotationKey"]!.Value<string>().ShouldBe(Key);
        exported["Hash"]!.Value<string>().ShouldBe("a1b2c3d4");
        exported["Content"]!["currency"]!.Value<string>().ShouldBe("NOK");
        exported["Sources"]!.Select(source => source["Id"]!.Value<string>()).ShouldBe(
        [
            "configuration:rst.invoicing",
            "configuration:sbt.northwind",
            "configuration:usg.northwind.invoicing",
            "configuration:cnt.northwind.prod",
            "template:exe",
            $"configuration:{Key}",
        ]);
        exported["Nodes"]!.Count().ShouldBe(6);
        exported["Steps"]!.Count().ShouldBe(7);
        exported["Origins"]!.Select(origin => (origin["Path"]!.Value<string>(), origin["Step"]!.Value<int>())).ShouldBe(
        [
            ("/currency", 4),
            ("/residency", 2),
            ("/plan", 3),
            ("/endpoint", 5),
            ("/mode", 6),
        ]);
    }

    [Fact]
    public void Options_AreParsedUnderConfig()
    {
        var command = Parse(CommandNames.COMPOSITION, Key, "-e", "composition.json", "-v", "staging");

        command.AnnotationKey.ShouldBe(Key);
        command.ExportFilePath.ShouldBe("composition.json");
        command.ViewName.ShouldBe("staging");
        Parse(CommandNames.COMPOSITION, Key, "--export", "other.json").ExportFilePath.ShouldBe("other.json");
        Parse(CommandNames.COMPOSITION, Key).ExportFilePath.ShouldBeNull();
    }

    // The execution example of inheritance.md: the subject sets currency NOK and residency norway, the usage currency USD
    // and plan enterprise, the context the endpoint, and the template of executions the mode. The subject is applied once
    // via the usage and once via the context; its second application sets the currency back to NOK, while the residency
    // keeps the first application, as the value is equal.
    private static ConfigurationComposition Execution()
    {
        return new ConfigurationComposition
        {
            AnnotationKey = Key,
            Hash = "a1b2c3d4",
            Content = JObject.Parse("""{ "currency": "NOK", "residency": "norway", "plan": "enterprise", "endpoint": "https://prod.northwind.example", "mode": "batch" }"""),
            Sources =
            [
                Configuration("rst.invoicing", ConfigurationCompositionSourceAnnotationType.Responsibility, ConfigurationCompositionSourcePresence.Missing, 0, null),
                Configuration("sbt.northwind", ConfigurationCompositionSourceAnnotationType.Subject, ConfigurationCompositionSourcePresence.Present, 3, """{ "currency": "NOK", "residency": "norway" }"""),
                Configuration("usg.northwind.invoicing", ConfigurationCompositionSourceAnnotationType.Usage, ConfigurationCompositionSourcePresence.Present, 2, """{ "currency": "USD", "plan": "enterprise" }"""),
                Configuration("cnt.northwind.prod", ConfigurationCompositionSourceAnnotationType.Context, ConfigurationCompositionSourcePresence.Present, 1, """{ "endpoint": "https://prod.northwind.example" }"""),
                new ConfigurationCompositionSource
                {
                    Id = "template:exe",
                    Kind = ConfigurationCompositionSourceKind.Template,
                    AnnotationType = ConfigurationCompositionSourceAnnotationType.Execution,
                    Presence = ConfigurationCompositionSourcePresence.Present,
                    Version = 4,
                    Modified = Stamp(),
                    Content = JObject.Parse("""{ "mode": "batch" }"""),
                },
                Configuration(Key, ConfigurationCompositionSourceAnnotationType.Execution, ConfigurationCompositionSourcePresence.Empty, 1, "{}"),
            ],
            Nodes =
            [
                Node(0, "rst.invoicing", 2, 2),
                Node(1, "sbt.northwind", 2, 2),
                Node(2, "usg.northwind.invoicing", 5, 1),
                Node(3, "sbt.northwind", 4, 2),
                Node(4, "cnt.northwind.prod", 5, 1),
                Node(5, Key, null, 0),
            ],
            Steps =
            [
                Step(1, "configuration:rst.invoicing", 0),
                Step(2, "configuration:sbt.northwind", 1),
                Step(3, "configuration:usg.northwind.invoicing", 2),
                Step(4, "configuration:sbt.northwind", 3),
                Step(5, "configuration:cnt.northwind.prod", 4),
                Step(6, "template:exe", 5),
                Step(7, $"configuration:{Key}", 5),
            ],
            Origins =
            [
                Origin("/currency", 4),
                Origin("/residency", 2),
                Origin("/plan", 3),
                Origin("/endpoint", 5),
                Origin("/mode", 6),
            ],
        };
    }

    // The grid of ExtendedConsole.WriteTable, rendered as text at the given width.
    private static IReadOnlyList<string> RenderTable(IEnumerable<string[]> rows, IReadOnlyList<IHeaderColumn> columns, int width)
    {
        var grid = new Grid { Stroke = LineThickness.None, Margin = new Thickness(1, 0) };
        grid.Columns.Add(columns.Select(column => column.Size).OfType<object>().ToArray());
        grid.Children.Add(columns.Select(column => new Cell(column.Content) { Stroke = LineThickness.None, Margin = new Thickness(1, 0) }).ToList());
        grid.Children.Add(new Cell(new Separator()) { ColumnSpan = columns.Count, Stroke = LineThickness.None });

        foreach (var row in rows)
        {
            grid.Children.Add(row.Select(text => new Cell(text) { Stroke = LineThickness.None, Margin = new Thickness(1, 0) }).OfType<object>().ToArray());
        }

        var text = ConsoleRenderer.RenderDocumentToText(new Document(grid), new TextRenderTarget(), new Rect(0, 0, width, Size.Infinity));
        return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    }

    private static ConfigurationCompositionSource Configuration(
        string key,
        ConfigurationCompositionSourceAnnotationType type,
        ConfigurationCompositionSourcePresence presence,
        long version,
        string? content)
    {
        return new ConfigurationCompositionSource
        {
            Id = $"configuration:{key}",
            Kind = ConfigurationCompositionSourceKind.Configuration,
            AnnotationType = type,
            AnnotationKey = key,
            Presence = presence,
            Version = version,
            Modified = content is null ? null : Stamp(),
            Content = content is null ? null : JObject.Parse(content),
        };
    }

    private static AuditStamp Stamp()
    {
        return new AuditStamp
        {
            At = new DateTimeOffset(2026, 10, 19, 8, 30, 0, TimeSpan.Zero),
            By = new AuditActor { Kind = AuditActorKind.Account, Id = "user_1", Name = "Ales Kobr" },
        };
    }

    private static ConfigurationCompositionNode Node(int index, string key, int? mergedInto, int depth)
    {
        return new ConfigurationCompositionNode { Index = index, AnnotationKey = key, MergedInto = mergedInto, Depth = depth };
    }

    private static ConfigurationCompositionStep Step(int order, string sourceId, int node)
    {
        return new ConfigurationCompositionStep { Order = order, SourceId = sourceId, Node = node };
    }

    private static ConfigurationCompositionOrigin Origin(string path, int step)
    {
        return new ConfigurationCompositionOrigin { Path = path, Step = step };
    }

    // The command that config selects, as the host builds it.
    private ConfigCompositionCommand Parse(params string[] args)
    {
        var services = new ServiceCollection()
            .AddSingleton(_fixture.Console.Object)
            .AddSingleton(_fixture.Context.Object)
            .AddSingleton(_api.Object)
            .AddSingleton(Mock.Of<IFileSystem>())
            .AddSingleton(GitHubActionsSettings.FromEnvironment(_ => null))
            .BuildServiceProvider();
        var app = new CommandLineApplication<ConfigGetCommand>(NullConsole.Singleton);
        app.Conventions.UseDefaultConventions().UseConstructorInjection(services);

        return app.Parse(args).SelectedCommand.ShouldBeOfType<CommandLineApplication<ConfigCompositionCommand>>().Model;
    }

    private void SetupComposition(ConfigurationComposition composition)
    {
        _api.Setup(api => api.GetConfigurationCompositionAsync("org1", "proj1", "default", Key, null)).ReturnsAsync(composition);
    }

    private Task<int> Execute(string key = Key, IFileSystem? fileSystem = null, string? exportFilePath = null)
    {
        var command = new ConfigCompositionCommand(_fixture.Console.Object, _fixture.Context.Object, _api.Object, fileSystem ?? Mock.Of<IFileSystem>())
        {
            AnnotationKey = key,
            ExportFilePath = exportFilePath,
        };

        return command.OnExecuteAsync();
    }
}
