using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Threading.Tasks;
using _42.CLI.Toolkit.Output;
using _42.Platform.Cli.Commands.Schemas;
using _42.Platform.Cli.Configuration;
using _42.Platform.Cli.Services;
using _42.Platform.Storyteller.Sdk;
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

    [Theory]
    [InlineData(400, ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(404, ExitCodes.ERROR_WRONG_INPUT)]
    [InlineData(403, ExitCodes.ERROR_CRASH)]
    [InlineData(500, ExitCodes.ERROR_CRASH)]
    public async Task Set_ErrorResponseWithAMessage_PrintsTheMessage(int statusCode, int expectedExitCode)
    {
        SetupSet(SchemaLayer.Type).ThrowsAsync(Error(statusCode, BadRequest, "The server refused it.", "SomeCode"));

        var exitCode = await Set(SchemaLayer.Type);

        exitCode.ShouldBe(expectedExitCode);
        Lines.ShouldBe(["! The server refused it.", "Error code: SomeCode"]);
    }

    [Theory]
    [InlineData("untyped")]
    [InlineData("no result")]
    [InlineData("blank message")]
    public async Task Set_ErrorWithoutAMessage_PrintsTheTextOfTheException(string kind)
    {
        var headers = new Dictionary<string, IEnumerable<string>>();
        ApiException exception = kind switch
        {
            "untyped" => new ApiException("The HTTP status code of the response was not expected (413).", 413, """{"Message":"Too large."}""", headers, null),
            "no result" => new ApiException<ErrorResponse>(BadRequest, 400, string.Empty, headers, null!, null),
            _ => new ApiException<ErrorResponse>(BadRequest, 400, string.Empty, headers, new ErrorResponse { Message = " ", ErrorCode = "InvalidKey" }, null),
        };
        SetupSet(SchemaLayer.Type).ThrowsAsync(exception);

        var exitCode = await Set(SchemaLayer.Type);

        exitCode.ShouldBe(ExitCodes.ERROR_CRASH);
        Lines.ShouldBe([$"Error occurred: {exception.Message}"]);
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
            .Setup(api => api.GetConfigurationSchemaAsync("org1", "proj1", "default", "exe"))
            .ThrowsAsync(Error(400, BadRequest, "Invalid annotation type code: exe"));

        var exitCode = await SchemaOperations.GetAsync(
            _fixture.Console.Object,
            _api.Object,
            _fixture.Context.Object,
            SchemaLayer.Type,
            "exe",
            null,
            null);

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        Lines.ShouldBe(["! Invalid annotation type code: exe"]);
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

        exitCode.ShouldBe(ExitCodes.ERROR_CRASH);
        Lines.ShouldBe(["! The schema was changed by another writer."]);
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
