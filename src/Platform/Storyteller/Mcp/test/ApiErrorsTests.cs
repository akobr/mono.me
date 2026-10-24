using _42.Platform.Storyteller.Mcp.Tools;
using _42.Platform.Storyteller.Mcp.UnitTests.Support;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

public class ApiErrorsTests
{
    private const string Marker = "SECRET-MARKER-42";

    private static readonly IReadOnlyDictionary<string, IEnumerable<string>> NoHeaders = new Dictionary<string, IEnumerable<string>>();

    [Fact]
    public void NotFoundWithAnErrorCode_KeepsMessageAndCode()
    {
        var result = ApiErrors.ToResult(Error(404, new Sdk.ErrorResponse { Message = "The key value 'db.host' was not found.", ErrorCode = "KeyValueNotFound" }));

        result.IsError.ShouldBe(true);
        result.Text().ShouldStartWith("Storyteller API answered 404 KeyValueNotFound: The key value 'db.host' was not found.");
        result.StructuredContent!.Value.GetProperty("Status").GetInt32().ShouldBe(404);
        result.StructuredContent.Value.GetProperty("ErrorCode").GetString().ShouldBe("KeyValueNotFound");
    }

    [Fact]
    public void PlainNotFound_HasAFixedMessage()
    {
        var result = ApiErrors.ToResult(new Sdk.ApiException("The HTTP status code of the response was not expected (404).", 404, Marker, NoHeaders, null));

        result.Text().ShouldContain("does not exist, or it is not visible");
        result.Text().ShouldNotContain(Marker);
    }

    [Fact]
    public void BadRequest_KeepsTheHint()
    {
        var result = ApiErrors.ToResult(Error(400, new Sdk.ErrorResponse { Message = "Invalid annotation key.", Hint = "Use rst.{name}.", ErrorCode = "InvalidKey" }));

        result.Text().ShouldBe("Storyteller API answered 400 InvalidKey: Invalid annotation key. Hint: Use rst.{name}.");
    }

    [Fact]
    public void SchemaViolation_ListsTheErrors()
    {
        var response = new Sdk.SchemaValidationErrorResponse
        {
            Message = "The configuration does not comply with the schema.",
            Errors = [new Sdk.SchemaValidationErrorDetail { AnnotationKey = "sbt.northwind", ViewName = "default", Errors = ["#/port: IntegerExpected"] }],
        };

        var result = ApiErrors.ToResult(new Sdk.ApiException<Sdk.SchemaValidationErrorResponse>("Conflict", 409, string.Empty, NoHeaders, response, null));

        var errors = result.StructuredContent!.Value.GetProperty("Errors");
        errors[0].GetProperty("AnnotationKey").GetString().ShouldBe("sbt.northwind");
        errors[0].GetProperty("Errors")[0].GetString().ShouldBe("#/port: IntegerExpected");
    }

    [Theory]
    [InlineData(409, "AnnotationDeleted")]
    [InlineData(412, "KeyValueVersionMismatch")]
    [InlineData(403, "AccessDenied")]
    public void ClientErrors_KeepTheirCode(int status, string code)
    {
        var result = ApiErrors.ToResult(Error(status, new Sdk.ErrorResponse { Message = "Refused.", ErrorCode = code }));

        result.Text().ShouldStartWith($"Storyteller API answered {status} {code}: Refused.");
    }

    [Fact]
    public void StructuredMessage_IsTheApiMessageAlone()
    {
        var result = ApiErrors.ToResult(Error(412, new Sdk.ErrorResponse { Message = "The key changed since it was read.", Hint = "Read the key again.", ErrorCode = "KeyValueVersionMismatch" }));

        var content = result.StructuredContent!.Value;
        content.GetProperty("Message").GetString().ShouldBe("The key changed since it was read.");
        content.GetProperty("Hint").GetString().ShouldBe("Read the key again.");
        content.GetProperty("ErrorCode").GetString().ShouldBe("KeyValueVersionMismatch");
        content.EnumerateObject().Select(property => property.Name).ShouldBe(["Status", "ErrorCode", "Message", "Hint"]);
        result.Text().ShouldBe("Storyteller API answered 412 KeyValueVersionMismatch: The key changed since it was read. Hint: Read the key again.");
    }

    [Theory]
    [InlineData(404, "It does not exist, or it is not visible to the signed-in person.")]
    [InlineData(500, ApiErrors.ServerMessage)]
    public void StructuredMessage_OfAnAnswerWithoutABody_IsTheFixedMessage(int status, string message)
    {
        var result = ApiErrors.ToResult(new Sdk.ApiException("The HTTP status code of the response was not expected.", status, Marker, NoHeaders, null));

        result.StructuredContent!.Value.GetProperty("Message").GetString().ShouldBe(message);
    }

    [Fact]
    public void Unauthorized_NamesTheDelegatedClientSetting()
    {
        var result = ApiErrors.ToResult(new Sdk.ApiException("Unauthorized", 401, "Scope(s): User.Impersonation", NoHeaders, null));

        result.Text().ShouldContain("Auth:DelegatedClients");
    }

    [Fact]
    public void BindingError_NeverQuotesTheApiMessage()
    {
        var result = ApiErrors.ToResult(Error(422, new Sdk.ErrorResponse { Message = $"Operand '{Marker}' is not a number.", Hint = Marker, ErrorCode = "binding.evaluation" }));

        result.Text().ShouldBe($"Storyteller API answered 422 binding.evaluation: {ApiErrors.BindingMessage}");
        result.StructuredContent!.Value.GetRawText().ShouldNotContain(Marker);
    }

    [Fact]
    public void ServerError_NeverQuotesTheApiMessageOrStackTrace()
    {
        var response = new Sdk.ErrorResponse
        {
            Message = $"Cosmos said {Marker}",
            Hint = Marker,
            Details = Marker,
            Error = new Sdk.ExceptionWrapper { Message = Marker, StackTrace = Marker },
        };

        var result = ApiErrors.ToResult(Error(500, response));

        result.Text().ShouldBe($"Storyteller API answered 500: {ApiErrors.ServerMessage}");
        result.StructuredContent!.Value.GetRawText().ShouldNotContain(Marker);
    }

    [Fact]
    public void ErrorWrapperAndDetails_AreNeverCopied()
    {
        var response = new Sdk.ErrorResponse
        {
            Message = "Refused.",
            ErrorCode = "Conflict",
            Details = Marker,
            Error = new Sdk.ExceptionWrapper { Message = Marker, StackTrace = Marker },
        };

        var result = ApiErrors.ToResult(Error(409, response));

        result.StructuredContent!.Value.GetRawText().ShouldNotContain(Marker);
        result.Text().ShouldNotContain(Marker);
    }

    private static Sdk.ApiException<Sdk.ErrorResponse> Error(int status, Sdk.ErrorResponse response)
    {
        return new Sdk.ApiException<Sdk.ErrorResponse>($"Status {status} {Marker}", status, Marker, NoHeaders, response, null);
    }
}
