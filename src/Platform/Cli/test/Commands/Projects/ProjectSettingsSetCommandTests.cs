using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Cli.Commands.Projects;
using Moq;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;
using SdkSettings = _42.Platform.Storyteller.Sdk.ProjectSettings;

namespace _42.Platform.Cli.UnitTests.Commands.Projects;

// sform project settings set: the merge patch of the file and the options (a PUT with --replace), the checks of the
// command line before any request, the dry run, and how the refusals of the API end.
public class ProjectSettingsSetCommandTests
{
    private const string ImportPath = "/work/settings.json";

    private readonly ProjectSettingsFixture _fixture = new();

    [Fact]
    public async Task Type_ByCode_SendsAMergePatch_AndSaysTheNewVersion()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        var sent = _fixture.SetupPatch(ProjectSettingsFixture.Changed(version: 4));

        var exitCode = await Command(command =>
        {
            command.TypeName = "exe";
            command.Label = "Job";
            command.Plural = "Jobs";
        }).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        var (patch, ifMatch) = sent.ShouldHaveSingleItem();
        ShouldBeJson(patch, """{"Types":{"exe":{"Label":"Job","Plural":"Jobs"}}}""");
        ifMatch.ShouldBeNull();
        _fixture.Lines.ShouldContain("# Changes");
        _fixture.Lines[^1].ShouldBe("Saved the project settings, version 4.");
        _fixture.VerifyRemembered(4);
    }

    [Theory]
    [InlineData("subject", "sbt")]
    [InlineData("SUBJECT", "sbt")]
    [InlineData("Unit of execution", "uxe")]
    [InlineData("RST", "rst")]
    public async Task Type_ByBuiltInName_IsItsCode(string type, string code)
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        var sent = _fixture.SetupPatch(ProjectSettingsFixture.Changed(version: 4));

        await Command(command =>
        {
            command.TypeName = type;
            command.Color = "rose";
        }).OnExecuteAsync();

        ShouldBeJson(sent.ShouldHaveSingleItem().Patch, "{\"Types\":{\"" + code + "\":{\"Color\":\"rose\"}}}");
    }

    [Fact]
    public async Task Type_ByAProjectLabel_IsRefused()
    {
        var exitCode = await Command(command =>
        {
            command.TypeName = "Company";
            command.Color = "rose";
        }).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Lines.ShouldHaveSingleItem().ShouldStartWith("! 'Company' is no annotation type; give a type code or its built-in name: rst (Responsibility), unt (Unit), sbt (Subject)");
    }

    [Fact]
    public async Task Icon_ByItsShortName_GetsThePrefix()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        var sent = _fixture.SetupPatch(ProjectSettingsFixture.Changed(version: 4));

        await Command(command =>
        {
            command.TypeName = "cnt";
            command.Icon = "building-2";
        }).OnExecuteAsync();

        ShouldBeJson(sent.ShouldHaveSingleItem().Patch, """{"Types":{"cnt":{"Icon":"i-lucide-building-2"}}}""");
    }

    // A plural needs its name, so an empty --label removes the stored plural too; the color and the icon stay.
    [Fact]
    public async Task EmptyLabel_RemovesThePluralToo()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        var sent = _fixture.SetupPatch(ProjectSettingsFixture.Changed(version: 4));

        await Command(command =>
        {
            command.TypeName = "sbt";
            command.Label = "";
        }).OnExecuteAsync();

        ShouldBeJson(sent.ShouldHaveSingleItem().Patch, """{"Types":{"sbt":{"Label":null,"Plural":null}}}""");
    }

    [Fact]
    public async Task Clear_RemovesTheEntryOfTheType()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        var sent = _fixture.SetupPatch(ProjectSettingsFixture.Changed(version: 4));

        await Command(command =>
        {
            command.TypeName = "sbt";
            command.IsClearRequested = true;
        }).OnExecuteAsync();

        ShouldBeJson(sent.ShouldHaveSingleItem().Patch, """{"Types":{"sbt":null}}""");
    }

    [Fact]
    public async Task Clear_WithAColor_LeavesOnlyThatColor()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        var sent = _fixture.SetupPatch(ProjectSettingsFixture.Changed(version: 4));

        await Command(command =>
        {
            command.TypeName = "sbt";
            command.IsClearRequested = true;
            command.Color = "gray";
        }).OnExecuteAsync();

        ShouldBeJson(sent.ShouldHaveSingleItem().Patch, """{"Types":{"sbt":{"Label":null,"Plural":null,"Color":"gray","Icon":null}}}""");
    }

    [Fact]
    public async Task TimeZoneAndDateFormat_AreSet_AndAnEmptyValueRemovesThem()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        var sent = _fixture.SetupPatch(ProjectSettingsFixture.Changed(version: 4));

        await Command(command =>
        {
            command.TimeZone = " America/Phoenix ";
            command.DateFormat = string.Empty;
            command.TypeName = "unt";
            command.Color = string.Empty;
        }).OnExecuteAsync();

        ShouldBeJson(sent.ShouldHaveSingleItem().Patch, """{"Types":{"unt":{"Color":null}},"DefaultTimeZone":"America/Phoenix","DateFormat":null}""");
    }

    [Fact]
    public async Task Replace_SendsTheResultAsAPut()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        var sent = _fixture.SetupPut(ProjectSettingsFixture.Changed(version: 4));

        var exitCode = await Command(command =>
        {
            command.IsReplaceRequested = true;
            command.TypeName = "exe";
            command.Label = "Job";
            command.DateFormat = "d mmm yyyy";
        }).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        var (body, ifMatch) = sent.ShouldHaveSingleItem();
        body.Types.Keys.ShouldBe(["exe"]);
        body.Types["exe"].Label.ShouldBe("Job");
        body.Types["exe"].Color.ShouldBeNull();
        body.DateFormat.ShouldBe("d mmm yyyy");
        body.DefaultTimeZone.ShouldBeNull();
        ifMatch.ShouldBeNull();
        _fixture.Lines[^1].ShouldBe("Saved the project settings, version 4.");
        _fixture.VerifyRemembered(4);
    }

    [Fact]
    public async Task Import_IsMergedFirst_ThenTheOptions()
    {
        // the camelCase file of the admin UI, as data sets ship it
        _fixture.FileSystem.AddFile(ImportPath, new MockFileData("""
            {
              // the exhibit set's types
              "types": { "SBT": { "label": "Company", "color": "PINK" }, "exe": { "label": "Job" } },
              "dateFormat": "dd/mm/yyyy",
            }
            """));
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        var sent = _fixture.SetupPatch(ProjectSettingsFixture.Changed(version: 4));

        await Command(command =>
        {
            command.ImportFilePath = ImportPath;
            command.TypeName = "sbt";
            command.Icon = "factory";
        }).OnExecuteAsync();

        ShouldBeJson(
            sent.ShouldHaveSingleItem().Patch,
            """{"Types":{"sbt":{"Label":"Company","Color":"pink","Icon":"i-lucide-factory"},"exe":{"Label":"Job"}},"DateFormat":"dd/mm/yyyy"}""");
    }

    [Fact]
    public async Task Import_ThatBreaksTheRules_ListsTheProblems_BeforeAnyRequest()
    {
        _fixture.FileSystem.AddFile(ImportPath, new MockFileData("""{ "Types": { "sbt": { "Color": "pinkish" } }, "DateFormat": "yy" }"""));

        var exitCode = await Command(command => command.ImportFilePath = ImportPath).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Lines[0].ShouldStartWith("! The file '");
        _fixture.Lines[0].ShouldEndWith("settings.json' holds no valid project settings:");
        _fixture.Lines.ShouldContain(line => line.StartsWith("! $.Types.sbt.Color: 'pinkish' is not a color of the catalog"));
        _fixture.Lines.ShouldContain(line => line.StartsWith("! $.DateFormat: 'yy' is not a date format"));
    }

    [Fact]
    public async Task Import_OfAMissingFile_IsRefused()
    {
        var exitCode = await Command(command => command.ImportFilePath = ImportPath).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Lines.ShouldHaveSingleItem().ShouldEndWith("settings.json' does not exist.");
    }

    [Fact]
    public async Task Import_ThatIsNoJson_IsRefused()
    {
        _fixture.FileSystem.AddFile(ImportPath, new MockFileData("Types: none"));

        var exitCode = await Command(command => command.ImportFilePath = ImportPath).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Lines.ShouldHaveSingleItem().ShouldContain("is not JSON");
    }

    [Fact]
    public async Task NoChangeOption_IsRefused()
    {
        var exitCode = await Command(command => command.IsReplaceRequested = true).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Lines.ShouldBe(["! Nothing to change: give -i|--import, --type with --label, --plural, --color, --icon or --clear, --time-zone, or --date-format."]);
    }

    [Fact]
    public async Task Type_WithoutAChange_IsRefused()
    {
        var exitCode = await Command(command => command.TypeName = "sbt").OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Lines.ShouldBe(["! --type sbt changes nothing on its own; add --label, --plural, --color, --icon or --clear."]);
    }

    [Fact]
    public async Task TypeOptions_WithoutAType_AreRefused()
    {
        var exitCode = await Command(command => command.Label = "Job").OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Lines.ShouldBe(["! --label, --plural, --color, --icon and --clear need --type, the type they change."]);
    }

    [Fact]
    public async Task ExpectedVersion_IsSentAsIfMatch()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        var sent = _fixture.SetupPatch(ProjectSettingsFixture.Changed(version: 4));

        var exitCode = await Command(command =>
        {
            command.TimeZone = "UTC";
            command.ExpectedVersion = 3;
        }).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        sent.ShouldHaveSingleItem().IfMatch.ShouldBe("\"3\"");
    }

    [Fact]
    public async Task ExpectedVersion_ThatIsStale_WritesNothing()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed(version: 4));

        var exitCode = await Command(command =>
        {
            command.TimeZone = "UTC";
            command.ExpectedVersion = 3;
        }).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Lines.ShouldBe(["! The project settings changed meanwhile; they are at version 4."]);
        _fixture.VerifyNothingRemembered();
    }

    [Fact]
    public async Task PreconditionFailed_NamesTheVersionTheSettingsAreAt()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        _fixture.SetupPatch(ProjectSettingsFixture.PreconditionFailed(5));

        var exitCode = await Command(command =>
        {
            command.TimeZone = "UTC";
            command.ExpectedVersion = 3;
        }).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Lines.ShouldBe(["! The project settings changed meanwhile; they are at version 5."]);
    }

    [Fact]
    public async Task InvalidSettings_ListEveryProblem()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        _fixture.SetupPatch(ProjectSettingsFixture.InvalidSettings(
            ("$.Types.sbt.Color", "color", "'pinkish' is not a color of the catalog."),
            ("$.DefaultTimeZone", "time-zone", "'CET-ish' is not an IANA time zone id.")));

        var exitCode = await Command(command =>
        {
            command.TypeName = "sbt";
            command.Color = "pinkish";
            command.TimeZone = "CET-ish";
        }).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Lines.ShouldBe([
            "! $.Types.sbt.Color: 'pinkish' is not a color of the catalog.",
            "! $.DefaultTimeZone: 'CET-ish' is not an IANA time zone id.",
        ]);
        _fixture.VerifyNothingRemembered();
    }

    [Fact]
    public async Task Refusal_PrintsTheServerMessage()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        _fixture.SetupPatch(ProjectSettingsFixture.Forbidden());

        var exitCode = await Command(command => command.DateFormat = "dd.mm.yyyy").OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        _fixture.Lines.ShouldBe(["! Changing the project settings needs a user with the Administrator role.", "Error code: AccessDenied"]);
    }

    [Fact]
    public async Task Refusal_OfTheRead_PrintsTheServerMessage()
    {
        _fixture.SetupRead(AnnotationCommandFixture.MissingScope("Annotation.Read"));

        var exitCode = await Command(command => command.DateFormat = "dd.mm.yyyy").OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        _fixture.Lines[0].ShouldBe($"! {AnnotationCommandFixture.MissingScopeMessage("Annotation.Read")}");
    }

    [Fact]
    public async Task AWriteThatChangesNothing_SaysTheVersionStays()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());
        _fixture.SetupPatch(ProjectSettingsFixture.Changed(version: 3));

        var exitCode = await Command(command => command.DateFormat = "dd.mm.yyyy").OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
        _fixture.Lines.ShouldBe(["The project settings did not change; they stay at version 3."]);
    }

    [Fact]
    public async Task DryRun_ShowsTheChange_AndWritesNothing()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());

        var exitCode = await Command(command =>
        {
            command.IsDryRun = true;
            command.TypeName = "subject";
            command.Label = "Client";
        }).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _fixture.Lines.ShouldBe(["# Changes", "Dry run: nothing was saved; the project settings stay at version 3."]);
        _fixture.Access.Verify(api => api.PatchProjectSettingsVersionedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<JToken>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        _fixture.VerifyNothingRemembered();
    }

    [Fact]
    public async Task DryRun_OfAnInvalidValue_ListsTheProblem()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());

        var exitCode = await Command(command =>
        {
            command.IsDryRun = true;
            command.TypeName = "sbt";
            command.Color = "pinkish";
        }).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Lines.ShouldHaveSingleItem().ShouldStartWith("! $.Types.sbt.Color: 'pinkish' is not a color of the catalog; use one of red, orange");
    }

    [Fact]
    public async Task DryRun_OfAReplace_ChecksTheWholeDocument()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());

        var exitCode = await Command(command =>
        {
            command.IsDryRun = true;
            command.IsReplaceRequested = true;
            command.TypeName = "sbt";
            command.Plural = "Companies";
        }).OnExecuteAsync();

        // the replace leaves the plural without its label
        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _fixture.Lines.ShouldHaveSingleItem().ShouldStartWith("! $.Types.sbt.Plural: A plural needs a label");
    }

    [Fact]
    public async Task DryRun_ThatChangesNothing_SaysSo()
    {
        _fixture.SetupRead(ProjectSettingsFixture.Changed());

        var exitCode = await Command(command =>
        {
            command.IsDryRun = true;
            command.TimeZone = "europe/prague";
        }).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
        _fixture.Lines.ShouldBe(["The project settings would not change; they stay at version 3."]);
    }

    private static void ShouldBeJson(JToken actual, string expected)
    {
        JToken.DeepEquals(actual, JToken.Parse(expected)).ShouldBeTrue($"{actual.ToString(Newtonsoft.Json.Formatting.None)} is not {expected}");
    }

    private ProjectSettingsSetCommand Command(System.Action<ProjectSettingsSetCommand> configure)
    {
        var command = new ProjectSettingsSetCommand(_fixture.Console.Object, _fixture.Context.Object, _fixture.Access.Object, _fixture.FileSystem, _fixture.Cache.Object);
        configure(command);
        return command;
    }
}
