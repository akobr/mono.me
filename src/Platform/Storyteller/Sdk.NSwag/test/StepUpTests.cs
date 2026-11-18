namespace _42.Platform.Storyteller.Sdk.UnitTests;

// The step-up 401 of the API (RFC 9470, 424-264): told apart from every other 401 by its error code or its challenge, with
// the window from either.
public class StepUpTests
{
    private const string StepUpJson = """
        { "Message": "This operation needs a recent sign-in: sign in again, then repeat it. A sign-in counts for 10 minutes.", "ErrorCode": "StepUpRequired", "Details": { "MaxAge": 600 } }
        """;

    private const string Challenge = "Bearer error=\"insufficient_user_authentication\", error_description=\"A recent sign-in is required\", max_age=\"300\"";

    [Fact]
    public void TypedErrorResponse_WithTheCode_IsAStepUp_WithTheMaxAgeOfTheChallenge()
    {
        var exception = new ApiException<ErrorResponse>("401", 401, StepUpJson, Headers(Challenge), new ErrorResponse { ErrorCode = "StepUpRequired" }, null);

        exception.IsStepUpRequired(out var maxAge).ShouldBeTrue();

        maxAge.ShouldBe(TimeSpan.FromSeconds(300));
    }

    // Browsers cannot read the header cross-origin; a client without it reads the window from the body.
    [Fact]
    public void PlainException_WithTheCodeInItsJson_IsAStepUp_WithTheMaxAgeOfTheBody()
    {
        var exception = new ApiException("401", 401, StepUpJson, Headers(), null);

        exception.IsStepUpRequired(out var maxAge).ShouldBeTrue();

        maxAge.ShouldBe(TimeSpan.FromMinutes(10));
    }

    [Theory]
    [InlineData("Bearer error=\"insufficient_user_authentication\", max_age=120", 120)]
    [InlineData("Bearer error=\"insufficient_user_authentication\", max_age=\"45\"", 45)]
    [InlineData("Bearer error=\"insufficient_user_authentication\"", null)]
    [InlineData("Bearer error=insufficient_user_authentication, error_description=\"not max_age=5, see \\\"max_age=7\\\"\", max_age=\"300\"", 300)]
    [InlineData("Bearer error_description=\"max_age=5\", error=\"insufficient_user_authentication\"", null)]
    [InlineData("Bearer realm=\"api\", error=\"insufficient_user_authentication\", x_max_age=9, max_age=60", 60)]
    [InlineData("Bearer error=\"invalid_token\", max_age=5, Bearer error=\"insufficient_user_authentication\", max_age=\"600\"", 600)]
    [InlineData("Basic realm=\"api\", Bearer error=\"insufficient_user_authentication\", max_age=120", 120)]
    [InlineData("Bearer realm=\"other\", max_age=5, Bearer error=\"insufficient_user_authentication\"", null)]
    public void Challenge_Alone_IsAStepUp(string challenge, int? seconds)
    {
        var exception = new ApiException("401", 401, string.Empty, Headers(challenge), null);

        exception.IsStepUpRequired(out var maxAge).ShouldBeTrue();

        maxAge.ShouldBe(seconds is null ? null : TimeSpan.FromSeconds(seconds.Value));
    }

    [Theory]
    [InlineData(401, "")]
    [InlineData(401, """{ "Message": "No.", "ErrorCode": "AccessDenied" }""")]
    [InlineData(401, "not json")]
    [InlineData(403, """{ "Message": "No.", "ErrorCode": "StepUpRequired" }""")]
    public void OtherResponses_AreNoStepUp(int status, string body)
    {
        var exception = new ApiException("no", status, body, Headers("Bearer error=\"invalid_token\""), null);

        exception.IsStepUpRequired(out var maxAge).ShouldBeFalse();
        exception.IsStepUpRequired().ShouldBeFalse();

        maxAge.ShouldBeNull();
    }

    // The error is read as a parameter: a description that mentions the step-up's error names no step-up.
    [Fact]
    public void Challenge_MentioningTheErrorOnlyInItsDescription_IsNoStepUp()
    {
        var challenge = "Bearer error=\"invalid_token\", error_description=\"not insufficient_user_authentication\", max_age=60";
        var exception = new ApiException("no", 401, string.Empty, Headers(challenge), null);

        exception.IsStepUpRequired(out var maxAge).ShouldBeFalse();

        maxAge.ShouldBeNull();
    }

    // The error names the step-up only in an OAuth challenge: a gateway's Basic challenge with it is no step-up.
    [Theory]
    [InlineData("Basic realm=\"gateway\", error=\"insufficient_user_authentication\", max_age=60")]
    [InlineData("Negotiate, Basic error=\"insufficient_user_authentication\"")]
    [InlineData("Digest realm=\"x\", nonce=\"abc==\", error=insufficient_user_authentication")]
    public void Error_OfAnotherScheme_IsNoStepUp(string challenge)
    {
        var exception = new ApiException("401", 401, string.Empty, Headers(challenge), null);

        exception.IsStepUpRequired(out var maxAge).ShouldBeFalse();

        maxAge.ShouldBeNull();
    }

    [Theory]
    [InlineData("DPoP error=\"insufficient_user_authentication\", max_age=90", 90)]
    [InlineData("Negotiate YIIabc==, bearer error=\"insufficient_user_authentication\", max_age=\"45\"", 45)]
    public void Error_OfAnOAuthScheme_IsAStepUp(string challenge, int seconds)
    {
        var exception = new ApiException("401", 401, string.Empty, Headers(challenge), null);

        exception.IsStepUpRequired(out var maxAge).ShouldBeTrue();

        maxAge.ShouldBe(TimeSpan.FromSeconds(seconds));
    }

    // A step-up named by its code takes the window of the only challenge, when it is an OAuth one, and of no challenge
    // when there are several, those without parameters counted.
    [Theory]
    [InlineData("Bearer max_age=\"120\"", 120)]
    [InlineData("Bearer realm=\"a\", max_age=120, Basic realm=\"b\"", 300)]
    [InlineData("Bearer max_age=\"120\", Negotiate", 300)]
    [InlineData("Negotiate, Bearer max_age=\"120\"", 300)]
    [InlineData("Basic max_age=\"120\"", 300)]
    public void Code_WithChallengesThatNameNoStepUp_TakesTheWindowOfTheOnlyOne(string challenge, int seconds)
    {
        var body = """{ "ErrorCode": "StepUpRequired", "Details": { "MaxAge": 300 } }""";
        var exception = new ApiException("401", 401, body, Headers(challenge), null);

        exception.IsStepUpRequired(out var maxAge).ShouldBeTrue();

        maxAge.ShouldBe(TimeSpan.FromSeconds(seconds));
    }

    // A typed body without its text (the generated client keeps both, a caller may not) still gives the window.
    [Fact]
    public void TypedErrorResponse_WithoutTextOrChallenge_TakesTheWindowOfItsDetails()
    {
        var result = new ErrorResponse { ErrorCode = "StepUpRequired", Details = Newtonsoft.Json.Linq.JObject.Parse("""{ "MaxAge": 900 }""") };
        var exception = new ApiException<ErrorResponse>("401", 401, string.Empty, Headers(), result, null);

        exception.IsStepUpRequired(out var maxAge).ShouldBeTrue();

        maxAge.ShouldBe(TimeSpan.FromMinutes(15));
    }

    // A typed Details of the caller's own (an anonymous object) holds an Int32, not the Int64 of parsed JSON.
    [Theory]
    [InlineData(600, 600)]
    [InlineData((short)90, 90)]
    [InlineData(600u, 600)]
    [InlineData(600ul, 600)]
    public void TypedErrorResponse_WithAnIntegerOfAnyType_TakesTheWindowOfItsDetails(object value, int seconds)
    {
        var result = new ErrorResponse { ErrorCode = "StepUpRequired", Details = new Dictionary<string, object> { ["MaxAge"] = value } };
        var exception = new ApiException<ErrorResponse>("401", 401, string.Empty, Headers(), result, null);

        exception.IsStepUpRequired(out var maxAge).ShouldBeTrue();

        maxAge.ShouldBe(TimeSpan.FromSeconds(seconds));
    }

    [Fact]
    public void TypedErrorResponse_WithAnAnonymousDetails_TakesItsWindow()
    {
        var result = new ErrorResponse { ErrorCode = "StepUpRequired", Details = new { MaxAge = 600 } };
        var exception = new ApiException<ErrorResponse>("401", 401, string.Empty, Headers(), result, null);

        exception.IsStepUpRequired(out var maxAge).ShouldBeTrue();

        maxAge.ShouldBe(TimeSpan.FromMinutes(10));
    }

    // A string, a fraction, a negative or an overflowing number is no window.
    [Theory]
    [InlineData("600")]
    [InlineData(600.5)]
    [InlineData(-1)]
    [InlineData(ulong.MaxValue)]
    public void TypedErrorResponse_WithAMaxAgeThatIsNoWindow_HasNone(object value)
    {
        var result = new ErrorResponse { ErrorCode = "StepUpRequired", Details = new Dictionary<string, object> { ["MaxAge"] = value } };
        var exception = new ApiException<ErrorResponse>("401", 401, string.Empty, Headers(), result, null);

        exception.IsStepUpRequired(out var maxAge).ShouldBeTrue();

        maxAge.ShouldBeNull();
    }

    // Whatever a proxy makes of the answer, the detection never throws: a window no TimeSpan holds, or a code that is
    // no string, gives no window or no step-up.
    [Theory]
    [InlineData("""{ "ErrorCode": { "x": 1 } }""", "Bearer error=\"invalid_token\"", false)]
    [InlineData("""{ "ErrorCode": "StepUpRequired", "Details": { "MaxAge": 99999999999999999 } }""", null, true)]
    [InlineData("""{ "ErrorCode": "StepUpRequired" }""", "Bearer error=\"insufficient_user_authentication\", max_age=99999999999999999", true)]
    public void MalformedAnswers_NeverThrow(string body, string? challenge, bool stepUp)
    {
        var exception = new ApiException("401", 401, body, Headers(challenge), null);

        exception.IsStepUpRequired(out var maxAge).ShouldBe(stepUp);

        maxAge.ShouldBeNull();
    }

    [Theory]
    [InlineData(600, "10 minutes")]
    [InlineData(60, "1 minute")]
    [InlineData(3600, "1 hour")]
    [InlineData(5400, "90 minutes")]
    [InlineData(90, "90 seconds")]
    public void DescribeWindow_WritesItAsTheApiDoes(int seconds, string expected)
    {
        StepUp.DescribeWindow(TimeSpan.FromSeconds(seconds)).ShouldBe(expected);
    }

    [Fact]
    public void SecretsWithheldReasons_AreTheValuesOfTheHeader()
    {
        SecretsWithheldReasons.MtlsRequired.ShouldBe("mtls-required");
        SecretsWithheldReasons.ActorRestricted.ShouldBe("actor-restricted");
        SecretsWithheldReasons.StepUpRequired.ShouldBe("step-up-required");
    }

    private static Dictionary<string, IEnumerable<string>> Headers(string? challenge = null)
    {
        var headers = new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase);

        if (challenge is not null)
        {
            headers["WWW-Authenticate"] = [challenge];
        }

        return headers;
    }
}
