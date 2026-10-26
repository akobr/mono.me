using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Net.Http.Headers;
using _42.Platform.Storyteller.Sdk.Credentials;
using _42.Platform.Storyteller.Sdk.Resilience;
using Microsoft.Extensions.Time.Testing;

namespace _42.Platform.Storyteller.Sdk.UnitTests.Credentials;

// A federated machine in a GitHub Actions job: the platform token is the job's ID token, requested from the runner for
// the audience of the token endpoint before every exchange.
public class GitHubActionsTokenTests
{
    private const string MachineId = "0b9f6a52-5d1c-4c47-9d0c-0f1e2d3c4b5a";
    private const string RequestUrl = "https://pipelines.actions.githubusercontent.com/abc/idtoken?api-version=2.0";
    private const string RequestToken = "github-request-t0ken";
    private const string TokenEndpoint = "https://api.example.com/api/v1/acme/billing/access/token";

    private const string Hint =
        "; the GitHub Actions token of this job was requested for the audience " + TokenEndpoint
        + ", which a rule of the machine must name exactly. For client credentials, set STORYTELLER_CLIENT_SECRET instead.";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 26, 8, 0, 0, TimeSpan.Zero));
    private readonly StubHttpHandler _http = new();
    private readonly MockFileSystem _fileSystem = new();
    private readonly Dictionary<string, string?> _variables = new()
    {
        ["STORYTELLER_CLIENT_ID"] = MachineId,
        ["ACTIONS_ID_TOKEN_REQUEST_URL"] = RequestUrl,
        ["ACTIONS_ID_TOKEN_REQUEST_TOKEN"] = RequestToken,
    };

    [Theory]
    [InlineData(RequestUrl, RequestUrl + "&audience=https%3A%2F%2Fapi.example.com%2Fapi%2Fv1%2Facme%2Fbilling%2Faccess%2Ftoken")]
    [InlineData("https://token.actions.example.com/idtoken", "https://token.actions.example.com/idtoken?audience=https%3A%2F%2Fapi.example.com%2Fapi%2Fv1%2Facme%2Fbilling%2Faccess%2Ftoken")]
    public async Task TheRunner_IsAskedForAnIdToken_ForTheTokenEndpoint(string requestUrl, string expectedUrl)
    {
        _variables["ACTIONS_ID_TOKEN_REQUEST_URL"] = requestUrl;
        _http.Respond(HttpStatusCode.OK, GitHubAnswer("gh-jwt-1"))
            .Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-1"));

        var authorization = await Bind(Create()).GetAuthorizationAsync(null, CancellationToken.None);

        authorization.ShouldBe(new AuthenticationHeaderValue("Bearer", "storyteller-1"));
        _http.Requests.Count.ShouldBe(2);
        var gitHub = _http.Requests[0];
        gitHub.Method.ShouldBe(HttpMethod.Get);
        gitHub.Uri.OriginalString.ShouldBe(expectedUrl);
        gitHub.Authorization.ShouldBe($"Bearer {RequestToken}");
        gitHub.Headers["Accept"].ShouldBe("application/json");
        var exchange = _http.Requests[1];
        exchange.Method.ShouldBe(HttpMethod.Post);
        exchange.Uri.ShouldBe(new Uri(TokenEndpoint));
        exchange.Form["subject_token"].ShouldBe("gh-jwt-1");
        exchange.Form["subject_token_type"].ShouldBe("urn:ietf:params:oauth:token-type:jwt");
        exchange.Form["client_id"].ShouldBe(MachineId);
    }

    [Fact]
    public async Task TheRequestToken_GoesToTheRunnerOnly_AndEveryExchangeGetsAFreshIdToken()
    {
        _variables["STORYTELLER_TOKEN_SCOPE"] = "Configuration.Read";
        _http.Respond(HttpStatusCode.OK, GitHubAnswer("gh-jwt-1"))
            .Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-1"))
            .Respond(HttpStatusCode.OK, GitHubAnswer("gh-jwt-2"))
            .Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-2"));
        var credential = Bind(Create());

        var first = await credential.GetAuthorizationAsync(null, CancellationToken.None);
        var renewed = await credential.GetAuthorizationAsync(first, CancellationToken.None);

        renewed!.Parameter.ShouldBe("storyteller-2");
        _http.Requests.Select(request => request.Method).ShouldBe([HttpMethod.Get, HttpMethod.Post, HttpMethod.Get, HttpMethod.Post]);
        _http.Requests[3].Form["subject_token"].ShouldBe("gh-jwt-2");
        _http.Requests[3].Form["scope"].ShouldBe("Configuration.Read");
        var exchanges = _http.Requests.Where(request => request.Method == HttpMethod.Post).ToList();
        exchanges.ShouldAllBe(request => request.Authorization == null);
        exchanges.ShouldAllBe(request => !request.Headers.Values.Any(value => value.Contains(RequestToken)));
        exchanges.ShouldAllBe(request => !request.Body!.Contains(RequestToken));
    }

    // The server compares the token's aud with the rule's audience byte for byte, and the rule names what sform built and
    // machine create printed: the endpoint as written, without its trailing slash. The exchange posts to the normalised URI.
    [Theory]
    [InlineData("https://API.Example.com/api/", "https://API.Example.com/api/v1/acme%20corp/billing.eu/access/token")]
    [InlineData("https://api.example.com:443/api//", "https://api.example.com:443/api/v1/acme%20corp/billing.eu/access/token")]
    [InlineData("http://LOCALHOST:7071/api", "http://LOCALHOST:7071/api/v1/acme%20corp/billing.eu/access/token")]
    public async Task TheAudience_IsTheTokenEndpoint_AsTheEndpointIsWritten(string endpoint, string audience)
    {
        _http.Respond(HttpStatusCode.OK, GitHubAnswer("gh-jwt-1"))
            .Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-1"));
        var credential = Create()
            .Bind(new StorytellerCredentialBinding(new Uri(endpoint)) { Organization = "acme corp", Project = "billing.eu", TimeProvider = _time })
            .WithPrimaryHandler(_http);

        await credential.GetAuthorizationAsync(null, CancellationToken.None);

        _http.Requests[0].Uri.OriginalString.ShouldBe($"{RequestUrl}&audience={Uri.EscapeDataString(audience)}");
        _http.Requests[1].Uri.AbsoluteUri.ShouldBe($"{new Uri(endpoint).AbsoluteUri.TrimEnd('/')}/v1/acme%20corp/billing.eu/access/token");
    }

    [Fact]
    public async Task AnUnboundCredential_TakesTheAudienceFromTheEndpointVariable_AsWritten()
    {
        _variables["STORYTELLER_API_URL"] = "  https://Storyteller.Example.com/api/  ";
        _variables["STORYTELLER_KEY"] = "@contoso.payments.default.sbt.northwind";
        _http.Respond(HttpStatusCode.OK, GitHubAnswer("gh-jwt-1"))
            .Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-1"));

        await Create().WithPrimaryHandler(_http).GetAuthorizationAsync(null, CancellationToken.None);

        _http.Requests[0].Uri.OriginalString.ShouldEndWith($"&audience={Uri.EscapeDataString("https://Storyteller.Example.com/api/v1/contoso/payments/access/token")}");
        _http.Requests[1].Uri.ShouldBe(new Uri("https://storyteller.example.com/api/v1/contoso/payments/access/token"));
    }

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task ABusyRunner_IsUnavailable_AfterRetries(HttpStatusCode status)
    {
        _http.Otherwise = (_, _) => Task.FromResult(StubHttpHandler.Response(status, """{ "message": "Try again later." }"""));

        var exception = await ShouldFail(StorytellerAuthenticationFailureReason.Unavailable);

        exception.Message.ShouldBe($"The GitHub Actions ID token request to https://pipelines.actions.githubusercontent.com/abc/idtoken failed with {(int)status}: Try again later.");
        _http.Requests.Count.ShouldBe(4);
        _http.Requests.ShouldAllBe(request => request.Method == HttpMethod.Get);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnUnreachableRunner_IsUnavailable(bool isTimeout)
    {
        _http.Otherwise = (_, _) => isTimeout
            ? Task.FromException<HttpResponseMessage>(new TaskCanceledException("The request timed out."))
            : Task.FromException<HttpResponseMessage>(new HttpRequestException("No such host is known."));

        var exception = await ShouldFail(StorytellerAuthenticationFailureReason.Unavailable);

        exception.Message.ShouldStartWith("The GitHub Actions ID token request to https://pipelines.actions.githubusercontent.com/abc/idtoken failed: ");
        _http.Requests.ShouldAllBe(request => request.Method == HttpMethod.Get);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{ "message": "Bad credentials" }""", "was rejected with 401: Bad credentials.")]
    [InlineData(HttpStatusCode.Forbidden, "", "was rejected with 403.")]
    [InlineData(HttpStatusCode.NotFound, "", "was rejected with 404.")]
    [InlineData(HttpStatusCode.Found, "", "was answered with a redirect (302), which is not followed.")]
    [InlineData(HttpStatusCode.OK, """{ "count": 0 }""", "returned no token.")]
    [InlineData(HttpStatusCode.OK, "<html/>", "returned no token.")]
    public async Task ARefusingRunner_IsRejected_WithoutARetry(HttpStatusCode status, string body, string ending)
    {
        _http.Respond(status, body.Length > 0 ? body : null, ("Location", "https://elsewhere.example.com/idtoken"));

        var exception = await ShouldFail(StorytellerAuthenticationFailureReason.Rejected);

        exception.Message.ShouldBe($"The GitHub Actions ID token request to https://pipelines.actions.githubusercontent.com/abc/idtoken {ending}");
        _http.Requests.ShouldHaveSingleItem().Method.ShouldBe(HttpMethod.Get);
    }

    // The audience is not a secret; the hint is the safety net of the GUID rule, as an Entra ID appId is a GUID too.
    [Fact]
    public async Task ARefusedExchange_NamesTheAudienceOfTheIdToken()
    {
        _http.Respond(HttpStatusCode.OK, GitHubAnswer("gh-jwt-s3cret"))
            .Respond(HttpStatusCode.BadRequest, """{ "error": "invalid_request", "error_description": "The subject token was not accepted." }""");

        var exception = await ShouldFail(StorytellerAuthenticationFailureReason.Rejected);

        exception.Message.ShouldBe($"The token request to {TokenEndpoint} was rejected with 400: invalid_request: The subject token was not accepted{Hint}");
        exception.InnerException.ShouldBeOfType<StorytellerAuthenticationException>();
        exception.Message.ShouldNotContain("gh-jwt-s3cret");
        exception.Message.ShouldNotContain(RequestToken);
    }

    [Fact]
    public async Task ARefusedExchange_OfATokenFile_HasNoHint()
    {
        _fileSystem.AddFile("/run/platform-token", new MockFileData("platform-token"));
        _variables["STORYTELLER_TOKEN_FILE"] = "/run/platform-token";
        _http.Respond(HttpStatusCode.BadRequest, """{ "error": "invalid_request" }""");

        var exception = await ShouldFail(StorytellerAuthenticationFailureReason.Rejected);

        exception.Message.ShouldBe($"The token request to {TokenEndpoint} was rejected with 400: invalid_request.");
        _http.Requests.ShouldHaveSingleItem().Method.ShouldBe(HttpMethod.Post);
    }

    [Fact]
    public async Task AnUnavailableExchange_HasNoHint()
    {
        _http.Respond(HttpStatusCode.OK, GitHubAnswer("gh-jwt-1"));
        _http.Otherwise = (_, _) => Task.FromResult(StubHttpHandler.Response(HttpStatusCode.ServiceUnavailable));

        var exception = await ShouldFail(StorytellerAuthenticationFailureReason.Unavailable);

        exception.Message.ShouldBe($"The token request to {TokenEndpoint} failed with 503.");
    }

    [Fact]
    public void TheCredential_PrintsItsSource_AndNoSecret()
    {
        var text = Create().ShouldBeOfType<FederatedTokenCredential>().ToString();

        text.ShouldBe($"StorytellerCredential {{ Kind = Federated, ClientId = {MachineId}, SubjectTokenSource = GitHubActions }}");
        text.ShouldNotContain(RequestToken);
    }

    private static string GitHubAnswer(string idToken)
    {
        return $$"""{"count":1,"value":"{{idToken}}"}""";
    }

    private StorytellerCredential Create()
    {
        return StorytellerCredential.FromEnvironment(name => _variables.GetValueOrDefault(name), _fileSystem, _time).ShouldNotBeNull();
    }

    private StorytellerCredential Bind(StorytellerCredential credential)
    {
        return credential
            .Bind(new StorytellerCredentialBinding(new Uri("https://api.example.com/api/"))
            {
                Organization = "acme",
                Project = "billing",
                TimeProvider = _time,
                Resilience = new StorytellerResilienceOptions { BaseDelay = TimeSpan.Zero },
            })
            .WithPrimaryHandler(_http);
    }

    private async Task<StorytellerAuthenticationException> ShouldFail(StorytellerAuthenticationFailureReason reason)
    {
        var exception = await Should.ThrowAsync<StorytellerAuthenticationException>(async () => await Bind(Create()).GetAuthorizationAsync(null, CancellationToken.None));

        exception.Reason.ShouldBe(reason);
        exception.Message.ShouldNotContain(RequestToken);
        exception.Message.ShouldNotContain("api-version");
        return exception;
    }
}
