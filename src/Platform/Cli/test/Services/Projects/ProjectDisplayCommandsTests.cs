using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Commands.Configuration;
using _42.Platform.Cli.Commands.KeyValues;
using _42.Platform.Cli.Commands.Storyteller;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services.Projects;
using _42.Platform.Cli.UnitTests.Commands;
using _42.Platform.Cli.UnitTests.Output;
using _42.Platform.Storyteller.Sdk;
using Moq;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Services.Projects;

// The places of spec section 7.2 with a project's display: list headers and tree tags, the Type column of story deleted,
// composition sources, import tables and the dates of the tables and the deleted notices. With the built-in display the
// commands write what they wrote before; input stays as it is.
public class ProjectDisplayCommandsTests
{
    private static readonly DateTimeOffset At = new(2026, 3, 5, 9, 7, 0, TimeSpan.Zero);

    private readonly AnnotationCommandFixture _fixture = new();

    private static ProjectDisplay Project => ProjectDisplayTests.Customized(TypeColorMode.Console, showsIcons: true, dateFormat: "dd.mm.yyyy");

    private static string LocalDate => At.ToLocalTime().ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

    private static string LocalTime => At.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("subjects", "# Companies", "# Subjects")]
    [InlineData("responsibilities", "# Policies", "# Responsibilities")]
    [InlineData("usages", "# Usages", "# Usages")]
    [InlineData("contexts", "# Contexts", "# Contexts")]
    [InlineData("executions", "# Jobs", "# Executions")]
    public async Task ListHeaders_AreThePluralsOfTheProject(string query, string withSettings, string builtIn)
    {
        SetupLists();

        (await List(Project, query)).ShouldBe(ExitCodes.SUCCESS);
        _fixture.Console.Lines.ShouldContain(withSettings);

        var builtInFixture = new ProjectDisplayCommandsTests();
        builtInFixture.SetupLists();
        await builtInFixture.List(ProjectDisplay.BuiltIn, query);
        builtInFixture._fixture.Console.Lines.ShouldContain(builtIn);
    }

    // The sentences keep the built-in names.
    [Fact]
    public async Task ListSentences_KeepTheBuiltInNames()
    {
        SetupLists();

        await List(Project, "subjects");

        _fixture.Console.Lines.ShouldContain("No subject exists yet.");
    }

    [Fact]
    public async Task TreeOfSubjects_WithSettings_ShowsTheProjectsNamesColorsAndIcons()
    {
        var console = await Tree(Project, root: null);

        console.Lines.ShouldContain(line => line.EndsWith("northwind \U0001F3E2 [Company]", StringComparison.Ordinal));
        console.Lines.ShouldContain(line => line.EndsWith("prod \U0001F4DA [context]", StringComparison.Ordinal));
        console.Lines.ShouldContain(line => line.EndsWith("invoicing \U0001F3AF [Policy] [disabled]", StringComparison.Ordinal));
        console.ColorOf("[Company]").ShouldBe(ConsoleColor.Magenta);
        console.ColorOf("[context]").ShouldBe(ConsoleColor.Cyan);
        console.ColorOf("[Policy]").ShouldBe(ConsoleColor.DarkMagenta);
        console.ColorOf("[disabled]").ShouldBe(ConsoleColor.DarkGray);
    }

    // Byte for byte the tree before project settings: the tags in the lowlight color.
    [Fact]
    public async Task TreeOfSubjects_BuiltIn_IsTodaysTree()
    {
        var console = await Tree(ProjectDisplay.BuiltIn, root: null);

        console.Text.ShouldBe(string.Join(Environment.NewLine,
        [
            "  Tree of subjects",
            "  └ northwind [subject]",
            "    └ prod [context]",
            "      └ invoicing [responsibility] [disabled]",
            string.Empty,
        ]));
        console.ColorOf("[subject]").ShouldBe(ConsoleColor.DarkGray);
        console.ColorOf("[context]").ShouldBe(ConsoleColor.DarkGray);
        console.ColorOf(" [responsibility] [disabled]").ShouldBe(ConsoleColor.DarkGray);
    }

    [Fact]
    public async Task TreeOfResponsibilities_WithSettings_ShowsTheProjectsNames()
    {
        var console = await Tree(Project, root: "rst");

        console.Lines.ShouldContain(line => line.EndsWith("invoicing \U0001F3AF [Policy]", StringComparison.Ordinal));
        console.Lines.ShouldContain(line => line.EndsWith("northwind \U0001F3E2 [Company]", StringComparison.Ordinal));
    }

    // A project that calls responsibilities Solution changes no input: --tree-root takes the codes and built-in names only.
    [Theory]
    [InlineData("solution")]
    [InlineData("policy")]
    [InlineData("company")]
    public async Task TreeRoot_IsNeverAName_OfTheProject(string root)
    {
        var settings = new StubProjectSettings(ProjectDisplay.Create(
            new Dictionary<string, _42.Platform.Storyteller.ProjectTypeAppearance>
            {
                ["rst"] = new() { Label = "Solution" },
                ["sbt"] = new() { Label = "Company" },
            },
            null,
            TypeColorMode.Console,
            false));
        _fixture.Console.UseInnerConsole(redirectedInput: false);

        var command = new StorytellerListCommand(_fixture.Console.Object, _fixture.Api.Object, _fixture.Context.Object, settings) { TreeBaseAnnotationType = root };
        (await command.OnExecuteAsync()).ShouldBe(ExitCodes.ERROR_WRONG_INPUT);

        _fixture.Console.ErrorText.ShouldContain($"Unknown tree root '{root}'; use subject (sbt) or responsibility (rst).");
        settings.Calls.ShouldBe(0);
        StorytellerListCommand.TryParseTreeRoot(root, out _).ShouldBeFalse();
    }

    [Fact]
    public async Task Deleted_TypeColumn_IsTheProjectsName_WithItsIcon_AndTheDatesInItsFormat()
    {
        SetupDeleted();

        await Deleted(Project);

        var table = _fixture.Console.Tables.ShouldHaveSingleItem();
        table.Headers.ShouldBe(["Key", "Type", "Deleted", "By", "Descendants", "Purged on"]);
        table.Rows[0].ShouldBe(["sbt.northwind", "\U0001F3E2 Company", LocalTime, "Ales Kobr", string.Empty, LocalDate]);
        table.Rows[1].ShouldBe(["cnt.northwind.prod", "\U0001F4DA Context", LocalTime, "Ales Kobr", string.Empty, LocalDate]);
        table.Rows[2].ShouldBe(["uxe.invoicing.batch.northwind.prod", "\U0001F4BB UnitOfExecution", LocalTime, "Ales Kobr", string.Empty, LocalDate]);
    }

    [Fact]
    public async Task Deleted_BuiltIn_IsTodaysTable()
    {
        SetupDeleted();

        await Deleted(ProjectDisplay.BuiltIn);

        var table = _fixture.Console.Tables.ShouldHaveSingleItem();
        table.Rows[0].ShouldBe(["sbt.northwind", "Subject", AuditConsoleExtensions.FormatTime(At), "Ales Kobr", string.Empty, AuditConsoleExtensions.FormatDate(At)]);
        table.Rows[2][1].ShouldBe("UnitOfExecution");
    }

    [Fact]
    public void CompositionSource_OfATemplate_IsTheProjectsName()
    {
        var composition = Composition(ConfigurationCompositionSourceAnnotationType.Execution);

        CompositionConsoleExtensions.GetStepRows(composition, Project)[0][2].ShouldBe("template Job");
        CompositionConsoleExtensions.GetStepRows(composition, ProjectDisplay.BuiltIn)[0][2].ShouldBe("template Execution");
        CompositionConsoleExtensions.GetStepRows(Composition(ConfigurationCompositionSourceAnnotationType.Unit), Project)[0][2].ShouldBe("template Unit");
    }

    [Fact]
    public async Task CompositionCommand_AsksForTheDisplay_AfterItsRead()
    {
        var api = new Mock<IConfigurationsApiClient>(MockBehavior.Strict);
        api.Setup(client => client.GetConfigurationCompositionAsync("org1", "proj1", "default", "exe.invoicing.northwind.prod", null))
            .ReturnsAsync(Composition(ConfigurationCompositionSourceAnnotationType.Execution));
        var settings = new StubProjectSettings(Project);

        var command = new ConfigCompositionCommand(_fixture.Console.Object, _fixture.Context.Object, api.Object, Mock.Of<System.IO.Abstractions.IFileSystem>(), settings)
        {
            AnnotationKey = "exe.invoicing.northwind.prod",
        };
        (await command.OnExecuteAsync()).ShouldBe(ExitCodes.SUCCESS);

        _fixture.Console.Tables[0].Rows[0][2].ShouldBe("template Job");
        settings.Calls.ShouldBe(1);
    }

    [Theory]
    [InlineData(ViewImportItemKind.Template, null, "exe", "Job")]
    [InlineData(ViewImportItemKind.Template, null, "Execution", "Job")]
    [InlineData(ViewImportItemKind.TypeSchema, null, "uxe", "uxe")]
    [InlineData(ViewImportItemKind.DescendantTypeSchema, "sbt.northwind", "exe", "Job below sbt.northwind")]
    [InlineData(ViewImportItemKind.Annotation, "sbt.northwind", "sbt", "sbt.northwind")]
    public void ImportDocument_IsNamedByTheProjectsName(ViewImportItemKind kind, string? key, string type, string expected)
    {
        var item = new ViewImportItem { Kind = kind, AnnotationKey = key, AnnotationType = type, Path = "file.json" };

        TransferConsoleExtensions.FormatDocument(item, Project).ShouldBe(expected);
        TransferConsoleExtensions.FormatDocument(item, ProjectDisplay.BuiltIn).ShouldBe(TransferConsoleExtensions.FormatDocument(item));
    }

    [Fact]
    public async Task History_ShowsTheTimesInTheProjectsFormat()
    {
        _fixture.Api
            .Setup(api => api.GetAnnotationHistoryAsync("org1", "proj1", "default", "sbt.northwind", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnnotationHistory
            {
                AnnotationKey = "sbt.northwind",
                Entries = [new AnnotationAuditEntry { At = At, Action = AnnotationAuditEntryAction.Created, Actor = new AuditActor { Kind = AuditActorKind.Account, Id = "u1", Name = "Ales Kobr" } }],
            });
        var settings = new StubProjectSettings(Project);

        await new StorytellerHistoryCommand(_fixture.Console.Object, _fixture.Context.Object, _fixture.Api.Object, settings) { AnnotationKey = "sbt.northwind" }.OnExecuteAsync();

        _fixture.Console.Tables.ShouldHaveSingleItem().Rows[0][0].ShouldBe(LocalTime);
    }

    // The JSON of the history is for programs: no request for the settings, the instants as they are.
    [Fact]
    public async Task HistoryJson_DoesNotAskForTheDisplay()
    {
        _fixture.Console.UseInnerConsole(redirectedInput: false);
        _fixture.Api
            .Setup(api => api.GetAnnotationHistoryAsync("org1", "proj1", "default", "sbt.northwind", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnnotationHistory { AnnotationKey = "sbt.northwind", Entries = [new AnnotationAuditEntry { At = At }] });
        var settings = new StubProjectSettings(Project);

        await new StorytellerHistoryCommand(_fixture.Console.Object, _fixture.Context.Object, _fixture.Api.Object, settings) { AnnotationKey = "sbt.northwind", Json = true }.OnExecuteAsync();

        settings.Calls.ShouldBe(0);
        _fixture.Console.OutputText.ShouldContain("2026-03-05T09:07:00");
    }

    [Fact]
    public void Versions_ShowTheTimesInTheProjectsFormat()
    {
        var console = new RecordingConsole();
        var versions = new[] { new ConfigurationVersion { Version = 2, Modified = new AuditStamp { At = At }, ExpirationTime = At } };

        console.Object.WriteVersions(versions, Project);

        console.Tables.ShouldHaveSingleItem().Rows[0][2].ShouldBe(LocalTime);
    }

    [Fact]
    public void KeyValueTables_ShowTheTimesInTheProjectsFormat()
    {
        var console = new RecordingConsole();
        var stamp = new AuditStamp { At = At, By = new AuditActor { Kind = AuditActorKind.System } };

        console.Object.WriteKeyValues(new KeyValuesResponse { Items = [new KeyValueSummary { Key = "a", Version = 1, Modified = stamp }] }, includeValues: false, showDeleted: false, At, Project);
        console.Object.WriteKeyValueVersions([new KeyValueVersion { Version = 1, Modified = stamp, IsCurrent = true }], Project);
        console.Object.WriteRecipients([new ClientEncryptionRecipient { Id = "r1", Name = "ci", PublicKey = null, Created = stamp }], Project);
        console.Object.WriteAudit(new KeyValueAuditResponse { Items = [new KeyValueAuditEvent { Time = At }] }, Project);

        console.Tables[0].Rows[0][^1].ShouldBe(LocalTime);
        console.Tables[1].Rows[0][3].ShouldBe(LocalTime);
        console.Tables[2].Rows[0][4].ShouldBe(LocalTime);
        console.Tables[3].Rows[0][0].ShouldBe(LocalTime);
        KeyValueTimes.Format(At, Project).ShouldBe(LocalTime);
    }

    [Fact]
    public void KeyValueUsage_NamesItsDaysInTheProjectsFormat()
    {
        var report = new KeyValueUsageReport
        {
            From = new DateTimeOffset(2026, 10, 16, 0, 0, 0, TimeSpan.Zero),
            To = new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero),
            Totals = new Dictionary<string, double>(),
        };
        var withSettings = new RecordingConsole();
        var builtIn = new RecordingConsole();

        withSettings.Object.WriteUsage(report, ProjectDisplay.Create(null, "d mmm yyyy", TypeColorMode.None, false));
        builtIn.Object.WriteUsage(report);

        withSettings.Lines[0].ShouldBe("# Key/value usage 16 Oct 2026 to 14 Nov 2026 (0 days with data)");
        builtIn.Lines[0].ShouldBe($"# Key/value usage {report.From:yyyy-MM-dd} to {report.To:yyyy-MM-dd} (0 days with data)");
    }

    [Fact]
    public async Task KeyValueAudit_AsksForTheDisplay()
    {
        var api = new Mock<IKeyValuesApiClient>();
        api.Setup(client => client.GetKeyValueAuditAsync("org1", "proj1", null, null, null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KeyValueAuditResponse { Items = [new KeyValueAuditEvent { Time = At }] });
        var settings = new StubProjectSettings(Project);

        await new KeyValueAuditCommand(_fixture.Console.Object, _fixture.Context.Object, api.Object, settings).OnExecuteAsync();

        _fixture.Console.Tables.ShouldHaveSingleItem().Rows[0][0].ShouldBe(LocalTime);
        settings.Calls.ShouldBe(1);
    }

    [Fact]
    public async Task GetOfADeletedAnnotation_NamesTheDatesInTheProjectsFormat()
    {
        _fixture.Api
            .Setup(api => api.GetAnnotationAsync("org1", "proj1", "default", "sbt.northwind", true, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(AnnotationCommandFixture.Error(404));
        _fixture.Api
            .Setup(api => api.GetDeletedAnnotationAsync("org1", "proj1", "default", "sbt.northwind", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Deleted("sbt.northwind"));

        await new StorytellerGetCommand(_fixture.Console.Object, _fixture.Context.Object, _fixture.Api.Object, Mock.Of<System.IO.Abstractions.IFileSystem>(), new StubProjectSettings(Project))
        {
            AnnotationKey = "sbt.northwind",
        }.OnExecuteAsync();

        _fixture.Line("The annotation").ShouldBe($"The annotation 'sbt.northwind' is deleted ({LocalTime} by Ales Kobr) and will be purged on {LocalDate}. Restore it with 'sform story restore sbt.northwind'.");
    }

    [Fact]
    public async Task Delete_NamesThePurgeDayInTheProjectsFormat()
    {
        _fixture.Api
            .Setup(api => api.DeleteAnnotationAsync("org1", "proj1", "default", "sbt.northwind", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnnotationDeletionResult { DeletedKeys = ["sbt.northwind"], Deletion = Deletion() });

        await new StorytellerDeleteCommand(_fixture.Console.Object, _fixture.Context.Object, _fixture.Api.Object, new StubProjectSettings(Project))
        {
            AnnotationKey = "sbt.northwind",
        }.OnExecuteAsync();

        _fixture.Line("Restore it").ShouldBe($"Restore it with 'sform story restore sbt.northwind' before {LocalDate}; it is purged automatically after that.");
    }

    private static AnnotationDeletion Deletion()
    {
        var deletion = AnnotationCommandFixture.Deletion("sbt.northwind");
        deletion.DeletedAt = At;
        deletion.PurgeAt = At;
        deletion.DescendantCount = null;
        return deletion;
    }

    private static Annotation Deleted(string key)
    {
        return AnnotationCommandFixture.Deleted(key, Deletion());
    }

    private static ConfigurationComposition Composition(ConfigurationCompositionSourceAnnotationType type)
    {
        return new ConfigurationComposition
        {
            AnnotationKey = "exe.invoicing.northwind.prod",
            Content = new JObject(),
            Sources = [new ConfigurationCompositionSource { Id = "template", Kind = ConfigurationCompositionSourceKind.Template, AnnotationType = type, Presence = ConfigurationCompositionSourcePresence.Present, Version = 4 }],
            Nodes = [new ConfigurationCompositionNode { Index = 0, AnnotationKey = "exe.invoicing.northwind.prod", Depth = 0 }],
            Steps = [new ConfigurationCompositionStep { Order = 1, SourceId = "template", Node = 0 }],
            Origins = [],
        };
    }

    private void SetupLists()
    {
        _fixture.Api.Setup(api => api.GetSubjectsAsync("org1", "proj1", "default", "x", null, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(new AnnotationsResponse_Subject { Annotations = [] });
        _fixture.Api.Setup(api => api.GetResponsibilitiesAsync("org1", "proj1", "default", "x", null, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(new AnnotationsResponse_Responsibility { Annotations = [] });
        _fixture.Api.Setup(api => api.GetUsagesAsync("org1", "proj1", "default", null, null, null, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(new AnnotationsResponse_Usage { Annotations = [] });
        _fixture.Api.Setup(api => api.GetContextsAsync("org1", "proj1", "default", null, "x", null, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(new AnnotationsResponse_Context { Annotations = [] });
        _fixture.Api.Setup(api => api.GetExecutionsAsync("org1", "proj1", "default", null, null, null, null, null, null, It.IsAny<CancellationToken>())).ReturnsAsync(new AnnotationsResponse_Execution { Annotations = [] });
    }

    private Task<int> List(ProjectDisplay display, string query)
    {
        var command = new StorytellerListCommand(_fixture.Console.Object, _fixture.Api.Object, _fixture.Context.Object, new StubProjectSettings(display));

        switch (query)
        {
            case "subjects":
                command.QuerySubjects = "x";
                break;
            case "responsibilities":
                command.QueryResponsibilities = "x";
                break;
            case "usages":
                command.QueryUsages = true;
                break;
            case "contexts":
                command.QueryContexts = "x";
                break;
            default:
                command.QueryExecutions = true;
                break;
        }

        return command.OnExecuteAsync();
    }

    // The tree as a console shows it: a subject, its context and a disabled responsibility (or the other way round).
    private async Task<ColorRecordingConsole> Tree(ProjectDisplay display, string? root)
    {
        var execution = new Execution { AnnotationKey = "exe.northwind.invoicing.prod", Name = "prod", IsDisabled = true };
        _fixture.Api.Setup(api => api.GetSubjectsAsync("org1", "proj1", "default", null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnnotationsResponse_Subject { Annotations = [new Subject { AnnotationKey = "sbt.northwind", Name = "northwind" }] });
        _fixture.Api.Setup(api => api.GetResponsibilitiesAsync("org1", "proj1", "default", null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnnotationsResponse_Responsibility { Annotations = [new Responsibility { AnnotationKey = "rst.invoicing", Name = "invoicing" }] });
        _fixture.Api.Setup(api => api.GetExecutionsAsync("org1", "proj1", "default", It.IsAny<string?>(), It.IsAny<string?>(), null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnnotationsResponse_Execution { Annotations = [execution] });

        var inner = new ColorRecordingConsole();
        var console = new SformConsole(inner, () => 120, _ => null);
        var command = new StorytellerListCommand(console, _fixture.Api.Object, _fixture.Context.Object, new StubProjectSettings(display))
        {
            ShowAsTree = true,
            TreeBaseAnnotationType = root,
        };

        (await command.OnExecuteAsync()).ShouldBe(ExitCodes.SUCCESS);
        return inner;
    }

    private void SetupDeleted()
    {
        _fixture.Api
            .Setup(api => api.GetDeletedAnnotationsAsync("org1", "proj1", "default", true, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AnnotationsResponse
            {
                Annotations = [Deleted("sbt.northwind"), Deleted("cnt.northwind.prod"), Deleted("uxe.invoicing.batch.northwind.prod")],
            });
    }

    private Task<int> Deleted(ProjectDisplay display)
    {
        return new StorytellerDeletedCommand(_fixture.Console.Object, _fixture.Context.Object, _fixture.Api.Object, new StubProjectSettings(display)).OnExecuteAsync();
    }
}
