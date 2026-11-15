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
    public void Unauthorized_PointsAtTheTokenExchange()
    {
        var result = ApiErrors.ToResult(new Sdk.ApiException("Unauthorized", 401, "Scope(s): User.Impersonation", NoHeaders, null));

        result.Text().ShouldBe($"Storyteller API answered 401: {ApiErrors.UnauthorizedMessage}");
        result.Text().ShouldContain("Auth:TokenExchange");
        result.Text().ShouldNotContain("DelegatedClients");
    }

    [Fact]
    public void ActorRestricted_SaysTheApiWithholdsSecretsFromTheAssistant()
    {
        var response = new Sdk.ErrorResponse
        {
            Message = $"The token acts for the person through an actor that may not reveal secrets. {Marker}",
            Hint = Marker,
            ErrorCode = ApiErrors.ActorRestrictedCode,
        };

        var result = ApiErrors.ToResult(Error(403, response));

        result.IsError.ShouldBe(true);
        result.Text().ShouldBe($"Storyteller API answered 403 ActorRestricted: {ApiErrors.ActorRestrictedMessage}");
        result.Text().ShouldContain("NoSecretReveal");
        result.Text().ShouldContain("withholds secret values");
        result.StructuredContent!.Value.GetProperty("ErrorCode").GetString().ShouldBe("ActorRestricted");
        result.StructuredContent.Value.GetProperty("Message").GetString().ShouldBe(ApiErrors.ActorRestrictedMessage);
        result.StructuredContent.Value.GetRawText().ShouldNotContain(Marker);
    }

    // The API refuses a machine with a Secrets scope (or its reset, or its federation rules) to the actor with the same
    // code: the text covers that refusal too, and does not send the person off to read secrets.
    [Fact]
    public void ActorRestricted_FromAMachineRoute_DoesNotTalkAboutReadingSecrets()
    {
        var response = new Sdk.ErrorResponse
        {
            ErrorCode = ApiErrors.ActorRestrictedCode,
            Message = "The token acts for the person through an actor that may not reveal secrets.",
        };

        var result = ApiErrors.ToResult(Error(403, response));

        result.Text().ShouldBe($"Storyteller API answered 403 ActorRestricted: {ApiErrors.ActorRestrictedMessage}");
        result.Text().ShouldContain("NoSecretReveal");
        result.Text().ShouldContain("sform");
        result.Text().ShouldContain("creating or resetting a machine with a Secrets scope");
        result.Text().ShouldNotContain("Read secret values");
    }

    // Another 403 keeps the API's own text: only ActorRestricted is about the assistant.
    [Fact]
    public void OtherForbidden_KeepsTheApiMessage()
    {
        var result = ApiErrors.ToResult(Error(403, new Sdk.ErrorResponse { Message = "Secret values are delivered to machines only over mTLS in this project.", ErrorCode = "MtlsRequired" }));

        result.Text().ShouldBe("Storyteller API answered 403 MtlsRequired: Secret values are delivered to machines only over mTLS in this project.");
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

    // 424-264: the step-up has its own fixed text, with the window written out; the API's message and Details are not copied.
    [Fact]
    public void StepUp_HasTheFixedTextWithTheWindow()
    {
        var response = new Sdk.ErrorResponse
        {
            Message = $"This operation needs a recent sign-in: sign in again, then repeat it. {Marker}",
            Hint = Marker,
            ErrorCode = "StepUpRequired",
            Details = Marker,
        };
        var exception = new Sdk.ApiException<Sdk.ErrorResponse>("Unauthorized", 401, string.Empty, Challenge(600), response, null);

        var result = ApiErrors.ToResult(exception);

        result.IsError.ShouldBe(true);
        result.Text().ShouldBe($"Storyteller API answered 401 StepUpRequired: {ApiErrors.StepUpRequiredMessage(TimeSpan.FromMinutes(10))}");
        result.Text().ShouldBe(
            "Storyteller API answered 401 StepUpRequired: The Storyteller API needs a recent sign-in of the person for this (within the last "
            + "10 minutes): reading or writing secret values, creating or resetting machine credentials, changing federation rules. A "
            + "reconnect that reuses the person's browser session keeps the old sign-in: ask the person to sign out at the identity "
            + "provider first (for AuthKit, Sign out in the admin UI), then reconnect the MCP server in the client and retry, or to make "
            + "the change in the admin UI. The assistant cannot do this itself.");
        var content = result.StructuredContent!.Value;
        content.GetProperty("Status").GetInt32().ShouldBe(401);
        content.GetProperty("ErrorCode").GetString().ShouldBe(ApiErrors.StepUpRequiredCode);
        content.GetProperty("MaxAge").GetInt64().ShouldBe(600);
        content.GetRawText().ShouldNotContain(Marker);
    }

    // A route that does not declare the body leaves it as text: the code and the window come from the JSON.
    [Fact]
    public void StepUp_OfAPlainException_ReadsTheCodeAndTheWindowOfItsJson()
    {
        var exception = new Sdk.ApiException(
            "Unauthorized",
            401,
            """{"Message":"This operation needs a recent sign-in.","ErrorCode":"StepUpRequired","Details":{"MaxAge":3600}}""",
            NoHeaders,
            null);

        var result = ApiErrors.ToResult(exception);

        result.Text().ShouldStartWith("Storyteller API answered 401 StepUpRequired: The Storyteller API needs a recent sign-in of the person for this (within the last 1 hour):");
        ApiErrors.GetErrorCode(exception).ShouldBe("StepUpRequired");
    }

    [Fact]
    public void StepUp_WithoutAWindow_LeavesItOut()
    {
        var exception = new Sdk.ApiException(
            "Unauthorized",
            401,
            string.Empty,
            new Dictionary<string, IEnumerable<string>> { ["WWW-Authenticate"] = ["Bearer error=\"insufficient_user_authentication\""] },
            null);

        var result = ApiErrors.ToResult(exception);

        result.Text().ShouldStartWith("Storyteller API answered 401 StepUpRequired: The Storyteller API needs a recent sign-in of the person for this: reading");
        result.StructuredContent!.Value.TryGetProperty("MaxAge", out _).ShouldBeFalse();
    }

    // The same code on a 403 is no step-up.
    [Fact]
    public void StepUpCode_OnAForbidden_KeepsTheApiMessage()
    {
        var result = ApiErrors.ToResult(Error(403, new Sdk.ErrorResponse { Message = "Refused.", ErrorCode = "StepUpRequired" }));

        result.Text().ShouldBe("Storyteller API answered 403 StepUpRequired: Refused.");
    }

    // Nothing else of a body left as text is read: its code reaches neither the result nor a log.
    [Fact]
    public void PlainException_OtherThanTheStepUp_KeepsNothingOfItsText()
    {
        var exception = new Sdk.ApiException("Forbidden", 403, $$"""{"Message":"{{Marker}}","ErrorCode":"{{Marker}}"}""", NoHeaders, null);

        ApiErrors.GetErrorCode(exception).ShouldBeNull();
        var result = ApiErrors.ToResult(exception);
        result.StructuredContent!.Value.GetRawText().ShouldNotContain(Marker);
        result.Text().ShouldNotContain(Marker);
    }

    [Theory]
    [InlineData(600, "10 minutes")]
    [InlineData(60, "1 minute")]
    [InlineData(3600, "1 hour")]
    [InlineData(7200, "2 hours")]
    [InlineData(90, "90 seconds")]
    public void StepUpWindow_IsWrittenOutAsTheApiWritesIt(int seconds, string expected)
    {
        ApiErrors.DescribeWindow(TimeSpan.FromSeconds(seconds)).ShouldBe(expected);
    }

    private static Dictionary<string, IEnumerable<string>> Challenge(int seconds)
    {
        return new Dictionary<string, IEnumerable<string>>
        {
            ["WWW-Authenticate"] = [$"Bearer error=\"insufficient_user_authentication\", error_description=\"A recent sign-in is required\", max_age=\"{seconds}\""],
        };
    }

    private static Sdk.ApiException<Sdk.ErrorResponse> Error(int status, Sdk.ErrorResponse response)
    {
        return new Sdk.ApiException<Sdk.ErrorResponse>($"Status {status} {Marker}", status, Marker, NoHeaders, response, null);
    }
}
