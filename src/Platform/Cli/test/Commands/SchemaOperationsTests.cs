using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Commands;
using _42.Platform.Cli.Commands.Schemas;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Output;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller.Sdk;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Moq.Language.Flow;
using Newtonsoft.Json.Linq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands;

// How the schema commands report a refusal of the API (424-69): the server's message and code of an ErrorResponse,
// the configurations of a failed compliance check, and the text of the exception when there is no message.
public class SchemaOperationsTests
{
    // the OpenAPI descriptions the SDK puts in the message of the exception, which the output must not show
    private const string BadRequest = "The request is not well formed.";
    private const string BadRequestDescendantType = "The request is not well formed, or the descendant type cannot descend from the annotation key (ErrorCode InvalidKey).";

    private const string CannotDescend = "The type rst cannot descend from exe.s.r.c, so it has no descendant-type schema there.";
    private const string InvalidSchema = "Invalid JSON Schema: Error converting value 5 to type 'JsonObjectType'. Path 'type'.";

    private readonly AnnotationCommandFixture _fixture = new();
    private readonly Mock<ISchemasApiClient> _api = new(MockBehavior.Strict);
    private readonly Mock<IEditorService> _editor = new(MockBehavior.Strict);

    private IReadOnlyList<string> Lines => _fixture.Console.Lines;

    [Fact]
    public async Task DescendantSet_TypeThatCannotDescend_PrintsTheMessageAndTheCode()
    {
        _api
            .Setup(api => api.SetDescendantTypeSchemaAsync("org1", "proj1", "default", "exe.s.r.c", "rst", false, It.IsAny<object>()))
            .ThrowsAsync(Error(400, BadRequestDescendantType, CannotDescend, "InvalidKey"));
        var fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            ["x.json"] = new("""{ "type": "object" }"""),
        });
        var command = new SchemaDescendantSetCommand(_fixture.Console.Object, _fixture.Context.Object, _api.Object, fileSystem)
        {
            AnnotationKey = "exe.s.r.c",
            AnnotationType = "rst",
            ImportFilePath = "x.json",
        };

        var exitCode = await command.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe([$"! {CannotDescend}", "Error code: InvalidKey"]);
        ShouldNotDumpTheException();
    }

    [Theory]
    [InlineData(nameof(SchemaLayer.Type))]
    [InlineData(nameof(SchemaLayer.Annotation))]
    [InlineData(nameof(SchemaLayer.Descendant))]
    public async Task Set_SchemaTheServerCannotParse_PrintsTheMessage(string layerName)
    {
        var layer = Enum.Parse<SchemaLayer>(layerName);
        SetupSet(layer).ThrowsAsync(Error(400, BadRequest, InvalidSchema));

        var exitCode = await Set(layer);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe([$"! {InvalidSchema}"]);
        ShouldNotDumpTheException();
    }

    [Theory]
    [InlineData(nameof(SchemaLayer.Type))]
    [InlineData(nameof(SchemaLayer.Annotation))]
    [InlineData(nameof(SchemaLayer.Descendant))]
    public async Task Edit_SaveOfASchemaTheServerCannotParse_PrintsTheMessage(string layerName)
    {
        var layer = Enum.Parse<SchemaLayer>(layerName);
        SetupGet(layer);
        SetupEditor();
        SetupSet(layer).ThrowsAsync(Error(400, BadRequest, InvalidSchema));

        var exitCode = await Edit(layer);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe([$"! {InvalidSchema}"]);
        ShouldNotDumpTheException();
    }

    [Fact]
    public async Task DescendantEdit_SaveOfATypeThatCannotDescend_PrintsTheMessageAndTheCode()
    {
        // the reads take any pair, so a schema stored before 424-59 opens, and only its save is refused
        SetupGet(SchemaLayer.Descendant, "exe.s.r.c", "rst");
        SetupEditor();
        _api
            .Setup(api => api.SetDescendantTypeSchemaAsync("org1", "proj1", "default", "exe.s.r.c", "rst", false, It.IsAny<object>()))
            .ThrowsAsync(Error(400, BadRequestDescendantType, CannotDescend, "InvalidKey"));

        var exitCode = await Edit(SchemaLayer.Descendant, "exe.s.r.c", "rst");

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe([$"! {CannotDescend}", "Error code: InvalidKey"]);
        ShouldNotDumpTheException();
    }

    // The schema writes declare no 404 today, so the case of a 404 only pins the mapping of 424-69 (as in
    // ConfigPatchCommand) for a write that declares it; an undeclared 404 is in the theory below. 424-215 (D2): a 403 is
    // 44 and a 500 a failure of the service (3), not a crash.
    [Theory]
    [InlineData(400, ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(404, ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(409, ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(403, ExitCodes.WARNING_UNAUTHORIZED_ACCESS)]
    [InlineData(500, ExitCodes.ERROR_SERVICE_FAILURE)]
    public async Task Set_ErrorResponseWithAMessage_PrintsTheMessage(int statusCode, int expectedExitCode)
    {
        SetupSet(SchemaLayer.Type).ThrowsAsync(Error(statusCode, BadRequest, "The server refused it.", "SomeCode"));

        var exitCode = await Set(SchemaLayer.Type);

        exitCode.ShouldBe(expectedExitCode);
        Lines.ShouldBe(["! The server refused it.", "Error code: SomeCode"]);
    }

    // 424-215: the message of the body when the client kept it as text, else the status; never the text of the exception,
    // and a rejected request is 2, not a crash.
    [Theory]
    [InlineData("undeclared 404", "! The request failed with 404 NotFound.")]
    [InlineData("undeclared 413", "! Too large.")]
    [InlineData("no result", "! The request failed with 400 BadRequest.")]
    [InlineData("blank message", "! The request failed with 400 BadRequest.", "Error code: InvalidKey")]
    public async Task Set_ErrorWithoutAMessage_PrintsTheStatus(string kind, params string[] expectedLines)
    {
        var headers = new Dictionary<string, IEnumerable<string>>();
        ApiException exception = kind switch
        {
            "undeclared 404" => new ApiException("The HTTP status code of the response was not expected (404).", 404, string.Empty, headers, null),
            "undeclared 413" => new ApiException("The HTTP status code of the response was not expected (413).", 413, """{"Message":"Too large."}""", headers, null),
            "no result" => new ApiException<ErrorResponse>(BadRequest, 400, string.Empty, headers, null!, null),
            _ => new ApiException<ErrorResponse>(BadRequest, 400, string.Empty, headers, new ErrorResponse { Message = " ", ErrorCode = "InvalidKey" }, null),
        };
        SetupSet(SchemaLayer.Type).ThrowsAsync(exception);

        var exitCode = await Set(SchemaLayer.Type);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe(expectedLines);
        ShouldNotDumpTheException();
    }

    [Fact]
    public async Task Set_FailedComplianceCheck_ListsTheConfigurations()
    {
        var response = new SchemaValidationErrorResponse
        {
            Message = "One or more existing configurations are not compliant with the provided schema.",
            Errors =
            [
                new SchemaValidationErrorDetail
                {
                    AnnotationKey = "exe.northwind.invoicing.prod",
                    ViewName = "default",
                    Errors = ["#/currency: PropertyRequired"],
                },
            ],
        };
        SetupSet(SchemaLayer.Type).ThrowsAsync(new ApiException<SchemaValidationErrorResponse>(
            "Existing configurations are not compliant with the provided schema.",
            409,
            string.Empty,
            new Dictionary<string, IEnumerable<string>>(),
            response,
            null));

        var exitCode = await Set(SchemaLayer.Type);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe(
        [
            "! One or more existing configurations are not compliant with the provided schema.",
            "exe.northwind.invoicing.prod (default)",
            "  #/currency: PropertyRequired",
        ]);
    }

    [Fact]
    public async Task Set_ConcurrentChange_PrintsTheServerMessage()
    {
        // The server sends an ErrorResponse, but the SDK reads every 409 of a write as a SchemaValidationErrorResponse:
        // a message and no entries, which TryWrite does not take; its message is printed (424-215).
        var exception = new ApiException<SchemaValidationErrorResponse>(
            "Existing configurations are not compliant with the provided schema. A concurrent modification is returned as ErrorResponse.",
            409,
            string.Empty,
            new Dictionary<string, IEnumerable<string>>(),
            new SchemaValidationErrorResponse { Message = "The schema was changed by another writer." },
            null);
        SetupSet(SchemaLayer.Type).ThrowsAsync(exception);

        var exitCode = await Set(SchemaLayer.Type);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe(["! The schema was changed by another writer."]);
    }

    // D2 of the CLI test cases (SCH-DEF-02, SCH-DEF-03): a Reader, or a machine without Schema.Write.
    [Theory]
    [InlineData(nameof(SchemaLayer.Type))]
    [InlineData(nameof(SchemaLayer.Annotation))]
    [InlineData(nameof(SchemaLayer.Descendant))]
    public async Task Set_Forbidden_IsUnauthorizedWithTheServerMessage(string layerName)
    {
        var layer = Enum.Parse<SchemaLayer>(layerName);
        SetupSet(layer).ThrowsAsync(AnnotationCommandFixture.MissingScope("Schema.Write"));

        var exitCode = await Set(layer);

        exitCode.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        Lines.ShouldBe([$"! {AnnotationCommandFixture.MissingScopeMessage("Schema.Write")}", "Error code: AccessDenied"]);
    }

    [Fact]
    public async Task Set_Unauthorized_LeavesTheOperation()
    {
        SetupSet(SchemaLayer.Type).ThrowsAsync(AnnotationCommandFixture.Error(401));

        var exception = await Should.ThrowAsync<ApiException>(() => Set(SchemaLayer.Type));

        exception.StatusCode.ShouldBe(401);
        Lines.ShouldBeEmpty();
    }

    [Fact]
    public async Task Get_BadRequest_PrintsTheMessage()
    {
        _api
            .Setup(api => api.GetConfigurationSchemaAsync("org1", "proj1", "default", "xyz"))
            .ThrowsAsync(Error(400, BadRequest, "Invalid annotation type: xyz"));

        var exitCode = await SchemaOperations.GetAsync(
            _fixture.Console.Object,
            _api.Object,
            _fixture.Context.Object,
            SchemaLayer.Type,
            "xyz",
            null,
            null);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe(["! Invalid annotation type: xyz"]);
    }

    [Fact]
    public async Task Get_NotFound_KeepsItsOwnSentence()
    {
        _api
            .Setup(api => api.GetConfigurationSchemaAsync("org1", "proj1", "default", "exe"))
            .ThrowsAsync(Error(404, "The schema has not been found.", "Schema not found."));

        var exitCode = await SchemaOperations.GetAsync(
            _fixture.Console.Object,
            _api.Object,
            _fixture.Context.Object,
            SchemaLayer.Type,
            "exe",
            null,
            null);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe(["The schema for 'exe' has not been found."]);
    }

    [Fact]
    public async Task Delete_ConcurrentChange_PrintsTheMessage()
    {
        _api
            .Setup(api => api.DeleteConfigurationSchemaAsync("org1", "proj1", "default", "exe"))
            .ThrowsAsync(Error(409, "A concurrent modification prevented the deletion.", "The schema was changed by another writer."));

        var exitCode = await SchemaOperations.DeleteAsync(
            _fixture.Console.Object,
            _api.Object,
            _fixture.Context.Object,
            SchemaLayer.Type,
            "exe",
            null);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe(["! The schema was changed by another writer."]);
    }

    // 424-221: schema type|annotation|descendant restore … --version N [-f|--force], through each command. Restored is 0,
    // nothing changed 42, and the subject is named as the other schema commands name it.
    [Theory]
    [InlineData(nameof(SchemaLayer.Type), "'exe'")]
    [InlineData(nameof(SchemaLayer.Annotation), "'rst.invoicing'")]
    [InlineData(nameof(SchemaLayer.Descendant), "'exe' under 'rst.invoicing'")]
    public async Task Restore_Restored_NamesTheNewVersion(string layerName, string subject)
    {
        var layer = Enum.Parse<SchemaLayer>(layerName);
        SetupRestore(layer, 2, false).ReturnsAsync(ConfigRestoreCommandTests.Result(VersionRestoreResultOutcome.Restored, 2, 4));

        var exitCode = await RestoreCommand(layer, 2, false).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        Lines.ShouldBe([$"! Restored v2 of the schema for {subject} as v4."]);
    }

    [Theory]
    [InlineData(nameof(SchemaLayer.Type), "'exe'")]
    [InlineData(nameof(SchemaLayer.Annotation), "'rst.invoicing'")]
    [InlineData(nameof(SchemaLayer.Descendant), "'exe' under 'rst.invoicing'")]
    public async Task Restore_CurrentVersion_IsAlreadyCurrent(string layerName, string subject)
    {
        var layer = Enum.Parse<SchemaLayer>(layerName);
        SetupRestore(layer, 4, false).ReturnsAsync(ConfigRestoreCommandTests.Result(VersionRestoreResultOutcome.Unchanged, 4, 4));

        var exitCode = await RestoreCommand(layer, 4, false).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
        Lines.ShouldBe([$"! v4 is already the current version of the schema for {subject}."]);
    }

    [Theory]
    [InlineData(nameof(SchemaLayer.Type), "'exe'")]
    [InlineData(nameof(SchemaLayer.Annotation), "'rst.invoicing'")]
    [InlineData(nameof(SchemaLayer.Descendant), "'exe' under 'rst.invoicing'")]
    public async Task Restore_VersionWithTheCurrentContent_ChangesNothing(string layerName, string subject)
    {
        var layer = Enum.Parse<SchemaLayer>(layerName);
        SetupRestore(layer, 1, false).ReturnsAsync(ConfigRestoreCommandTests.Result(VersionRestoreResultOutcome.Unchanged, 1, 4));

        var exitCode = await RestoreCommand(layer, 1, false).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_NO_WORK_NEEDED);
        Lines.ShouldBe([$"! v1 equals the current version v4 of the schema for {subject}; nothing changed."]);
    }

    [Theory]
    [InlineData(nameof(SchemaLayer.Type))]
    [InlineData(nameof(SchemaLayer.Annotation))]
    [InlineData(nameof(SchemaLayer.Descendant))]
    public async Task Restore_Force_IsPassedThrough(string layerName)
    {
        var layer = Enum.Parse<SchemaLayer>(layerName);
        SetupRestore(layer, 1, true).ReturnsAsync(ConfigRestoreCommandTests.Result(VersionRestoreResultOutcome.Restored, 1, 5));

        var exitCode = await RestoreCommand(layer, 1, true).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        Lines.ShouldBe([$"! Restored v1 of the schema for {SchemaOperations.Describe(layer, Target(layer).Type, Target(layer).Key)} as v5."]);
    }

    [Theory]
    [InlineData(nameof(SchemaLayer.Type))]
    [InlineData(nameof(SchemaLayer.Annotation))]
    [InlineData(nameof(SchemaLayer.Descendant))]
    public async Task Restore_WithoutVersion_SaysSoBeforeAnyCall(string layerName)
    {
        var exitCode = await RestoreCommand(Enum.Parse<SchemaLayer>(layerName), null, false).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe([$"! {VersionRestoreConsole.MissingVersionMessage}"]);
        _api.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(nameof(SchemaLayer.Type))]
    [InlineData(nameof(SchemaLayer.Annotation))]
    [InlineData(nameof(SchemaLayer.Descendant))]
    public async Task Restore_NegativeVersion_SaysSoBeforeAnyCall(string layerName)
    {
        var exitCode = await RestoreCommand(Enum.Parse<SchemaLayer>(layerName), -3, false).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe(["! The version to restore must be 1 or more, not -3; 'versions' lists them."]);
        _api.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(nameof(SchemaLayer.Type))]
    [InlineData(nameof(SchemaLayer.Annotation))]
    [InlineData(nameof(SchemaLayer.Descendant))]
    public async Task Restore_FailedComplianceCheck_ListsTheConfigurations(string layerName)
    {
        var layer = Enum.Parse<SchemaLayer>(layerName);
        SetupRestore(layer, 1, false).ThrowsAsync(new ApiException<SchemaValidationErrorResponse>(
            "Configurations do not comply with the version.",
            409,
            string.Empty,
            new Dictionary<string, IEnumerable<string>>(),
            new SchemaValidationErrorResponse
            {
                Message = "One or more existing configurations are not compliant with the provided schema.",
                Errors = [new SchemaValidationErrorDetail { AnnotationKey = "exe.northwind.invoicing.prod", ViewName = "default", Errors = ["#/req: PropertyRequired"] }],
            },
            null));

        var exitCode = await RestoreCommand(layer, 1, false).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe(
        [
            "! One or more existing configurations are not compliant with the provided schema.",
            "exe.northwind.invoicing.prod (default)",
            "  #/req: PropertyRequired",
        ]);
    }

    [Theory]
    [InlineData(nameof(SchemaLayer.Type))]
    [InlineData(nameof(SchemaLayer.Annotation))]
    [InlineData(nameof(SchemaLayer.Descendant))]
    public async Task Restore_UnknownVersion_PrintsTheMessageAndTheCode(string layerName)
    {
        var layer = Enum.Parse<SchemaLayer>(layerName);
        SetupRestore(layer, 9, false).ThrowsAsync(Error(404, "The version was never written or has expired (ErrorCode VersionNotFound).", "The version 9 does not exist or has expired.", "VersionNotFound"));

        var exitCode = await RestoreCommand(layer, 9, false).OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe(["! The version 9 does not exist or has expired.", "Error code: VersionNotFound"]);
        ShouldNotDumpTheException();
    }

    [Fact]
    public async Task DescendantRestore_TypeThatCannotDescend_PrintsTheMessageAndTheCode()
    {
        _api
            .Setup(api => api.RestoreDescendantTypeSchemaVersionAsync("org1", "proj1", "default", "exe.s.r.c", "rst", 1, true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(Error(400, BadRequestDescendantType, CannotDescend, "InvalidKey"));
        var command = new SchemaDescendantRestoreCommand(_fixture.Console.Object, _fixture.Context.Object, _api.Object)
        {
            AnnotationKey = "exe.s.r.c",
            AnnotationType = "rst",
            Version = 1,
            Force = true,
        };

        var exitCode = await command.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe([$"! {CannotDescend}", "Error code: InvalidKey"]);
        ShouldNotDumpTheException();
    }

    [Fact]
    public async Task Restore_Unauthorized_LeavesTheOperation()
    {
        SetupRestore(SchemaLayer.Type, 1, false).ThrowsAsync(AnnotationCommandFixture.Error(401));

        var exception = await Should.ThrowAsync<ApiException>(() => RestoreCommand(SchemaLayer.Type, 1, false).OnExecuteAsync());

        exception.StatusCode.ShouldBe(401);
        Lines.ShouldBeEmpty();
    }

    [Fact]
    public void Restore_OptionsAreParsed()
    {
        var type = ParseRestore<SchemaTypeCommand, SchemaTypeRestoreCommand>("restore", "exe", "--version", "2", "-f");
        type.AnnotationType.ShouldBe("exe");
        type.Version.ShouldBe(2);
        type.Force.ShouldBeTrue();

        var annotation = ParseRestore<SchemaAnnotationCommand, SchemaAnnotationRestoreCommand>("restore", "rst.invoicing", "--version", "3", "--force", "-v", "staging");
        annotation.AnnotationKey.ShouldBe("rst.invoicing");
        annotation.Version.ShouldBe(3);
        annotation.Force.ShouldBeTrue();
        annotation.ViewName.ShouldBe("staging");

        var descendant = ParseRestore<SchemaDescendantCommand, SchemaDescendantRestoreCommand>("restore", "rst.invoicing", "unt", "--version", "1");
        descendant.AnnotationKey.ShouldBe("rst.invoicing");
        descendant.AnnotationType.ShouldBe("unt");
        descendant.Version.ShouldBe(1);
        descendant.Force.ShouldBeFalse();
    }

    // An ErrorResponse as the SDK throws it: the message of the exception is the OpenAPI description of the status.
    private static ApiException<ErrorResponse> Error(int statusCode, string description, string message, string? errorCode = null)
    {
        return new ApiException<ErrorResponse>(
            description,
            statusCode,
            string.Empty,
            new Dictionary<string, IEnumerable<string>>(),
            new ErrorResponse { Message = message, ErrorCode = errorCode },
            null);
    }

    private static (string? Type, string? Key) Target(SchemaLayer layer)
    {
        return layer switch
        {
            SchemaLayer.Type => ("exe", null),
            SchemaLayer.Annotation => (null, "rst.invoicing"),
            _ => ("exe", "rst.invoicing"),
        };
    }

    private void ShouldNotDumpTheException()
    {
        Lines.ShouldNotContain(line => line.Contains("Error occurred:"));
        Lines.ShouldNotContain(line => line.Contains("Status: 400"));
        Lines.ShouldNotContain(line => line.Contains(BadRequest));
    }

    private ISetup<ISchemasApiClient, Task<VersionRestoreResult>> SetupRestore(SchemaLayer layer, int version, bool force)
    {
        var (type, key) = Target(layer);

        return layer switch
        {
            SchemaLayer.Type => _api.Setup(api => api.RestoreSchemaVersionAsync("org1", "proj1", "default", type!, version, force, It.IsAny<CancellationToken>())),
            SchemaLayer.Annotation => _api.Setup(api => api.RestoreAnnotationSchemaVersionAsync("org1", "proj1", "default", key!, version, force, It.IsAny<CancellationToken>())),
            _ => _api.Setup(api => api.RestoreDescendantTypeSchemaVersionAsync("org1", "proj1", "default", key!, type!, version, force, It.IsAny<CancellationToken>())),
        };
    }

    // The restore command of the layer, as the user types it: schema type|annotation|descendant restore.
    private BaseContextCommand RestoreCommand(SchemaLayer layer, int? version, bool force)
    {
        var (type, key) = Target(layer);

        return layer switch
        {
            SchemaLayer.Type => new SchemaTypeRestoreCommand(_fixture.Console.Object, _fixture.Context.Object, _api.Object)
            {
                AnnotationType = type!,
                Version = version,
                Force = force,
            },
            SchemaLayer.Annotation => new SchemaAnnotationRestoreCommand(_fixture.Console.Object, _fixture.Context.Object, _api.Object)
            {
                AnnotationKey = key!,
                Version = version,
                Force = force,
            },
            _ => new SchemaDescendantRestoreCommand(_fixture.Console.Object, _fixture.Context.Object, _api.Object)
            {
                AnnotationKey = key!,
                AnnotationType = type!,
                Version = version,
                Force = force,
            },
        };
    }

    // The restore command that the group selects, as the host builds it.
    private TRestore ParseRestore<TGroup, TRestore>(params string[] args)
        where TGroup : class
        where TRestore : class
    {
        var services = new ServiceCollection()
            .AddSingleton(_fixture.Console.Object)
            .AddSingleton(_fixture.Context.Object)
            .AddSingleton(_api.Object)
            .BuildServiceProvider();
        var app = new CommandLineApplication<TGroup>(NullConsole.Singleton);
        app.Conventions.UseDefaultConventions().UseConstructorInjection(services);

        return app.Parse(args).SelectedCommand.ShouldBeOfType<CommandLineApplication<TRestore>>().Model;
    }

    private ISetup<ISchemasApiClient, Task<ConfigurationSchema>> SetupSet(SchemaLayer layer)
    {
        var (type, key) = Target(layer);

        return layer switch
        {
            SchemaLayer.Type => _api.Setup(api => api.SetConfigurationSchemaAsync("org1", "proj1", "default", type!, false, It.IsAny<object>())),
            SchemaLayer.Annotation => _api.Setup(api => api.SetAnnotationSchemaAsync("org1", "proj1", "default", key!, false, It.IsAny<object>())),
            _ => _api.Setup(api => api.SetDescendantTypeSchemaAsync("org1", "proj1", "default", key!, type!, false, It.IsAny<object>())),
        };
    }

    private void SetupGet(SchemaLayer layer, string? key = null, string? type = null)
    {
        var target = Target(layer);
        key ??= target.Key;
        type ??= target.Type;
        var stored = new ConfigurationSchema { Version = 1, Content = new JObject { ["type"] = "object" } };

        _ = layer switch
        {
            SchemaLayer.Type => _api.Setup(api => api.GetConfigurationSchemaAsync("org1", "proj1", "default", type!)).ReturnsAsync(stored),
            SchemaLayer.Annotation => _api.Setup(api => api.GetAnnotationSchemaAsync("org1", "proj1", "default", key!)).ReturnsAsync(stored),
            _ => _api.Setup(api => api.GetDescendantTypeSchemaAsync("org1", "proj1", "default", key!, type!)).ReturnsAsync(stored),
        };
    }

    private void SetupEditor()
    {
        _editor
            .Setup(editor => editor.EditJsonAsync(
                It.IsAny<IExtendedConsole>(),
                It.IsAny<EditorOptions>(),
                It.IsAny<JObject>(),
                It.IsAny<string>(),
                false,
                It.IsAny<bool>()))
            .ReturnsAsync(new JsonEditResult(new JObject { ["type"] = 5 }, ExitCodes.SUCCESS));
    }

    private Task<int> Set(SchemaLayer layer)
    {
        var (type, key) = Target(layer);

        return SchemaOperations.SetAsync(
            _fixture.Console.Object,
            _api.Object,
            _fixture.Context.Object,
            layer,
            type,
            key,
            new JObject { ["type"] = 5 },
            false);
    }

    private Task<int> Edit(SchemaLayer layer, string? key = null, string? type = null)
    {
        var target = Target(layer);

        return SchemaOperations.EditAsync(
            _fixture.Console.Object,
            _api.Object,
            _fixture.Context.Object,
            _editor.Object,
            new EditorOptions(),
            layer,
            type ?? target.Type,
            key ?? target.Key,
            false);
    }
}
