using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Net.Http.Headers;
using _42.Platform.Storyteller.Sdk.Credentials;
using _42.Platform.Storyteller.Sdk.Resilience;
using Microsoft.Extensions.Time.Testing;
using Polly.CircuitBreaker;

namespace _42.Platform.Storyteller.Sdk.UnitTests.Credentials;

public class FederatedTokenTests
{
    private const string MachineId = "0b9f6a52-5d1c-4c47-9d0c-0f1e2d3c4b5a";
    private const string SubjectPath = "/var/run/secrets/tokens/storyteller";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero));
    private readonly StubHttpHandler _http = new();
    private readonly MockFileSystem _fileSystem = new();

    public FederatedTokenTests()
    {
        _fileSystem.AddFile(SubjectPath, new MockFileData("platform-token-1\n"));
    }

    [Fact]
    public async Task TheExchange_PostsTheFormToTheTokenEndpointOfTheProject()
    {
        _http.Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-1"));
        var credential = Bind(Create(new FederatedTokenOptions
        {
            ClientId = MachineId,
            SubjectTokenPath = SubjectPath,
            SubjectTokenType = "id_token",
            Scope = "Configuration.Read",
            FileSystem = _fileSystem,
        }));

        var authorization = await credential.GetAuthorizationAsync(null, CancellationToken.None);

        authorization.ShouldBe(new AuthenticationHeaderValue("Bearer", "storyteller-1"));
        var request = _http.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.ShouldBe(new Uri("https://api.example.com/api/v1/acme/billing/access/token"));
        request.Authorization.ShouldBeNull();
        request.Headers.ContainsKey("Authorization").ShouldBeFalse();
        request.ContentType.ShouldBe("application/x-www-form-urlencoded");
        request.Form.ShouldBe(new Dictionary<string, string>
        {
            ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
            ["client_id"] = MachineId,
            ["subject_token"] = "platform-token-1",
            ["subject_token_type"] = "urn:ietf:params:oauth:token-type:id_token",
            ["scope"] = "Configuration.Read",
        });
    }

    [Fact]
    public async Task TheSubjectToken_IsReadAgainForEveryExchange()
    {
        _http.Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-1"));
        _http.Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-2"));
        var credential = Bind(Create());

        var first = await credential.GetAuthorizationAsync(null, CancellationToken.None);
        _fileSystem.File.WriteAllText(SubjectPath, "platform-token-2");
        await credential.GetAuthorizationAsync(first, CancellationToken.None);

        _http.Requests.Select(request => request.Form["subject_token"]).ShouldBe(["platform-token-1", "platform-token-2"]);
        _http.Requests[0].Form["subject_token_type"].ShouldBe("urn:ietf:params:oauth:token-type:jwt");
    }

    [Fact]
    public void TheTokenClient_NeverFollowsARedirect()
    {
        using var handler = TokenEndpointClient.CreatePrimaryHandler(handler => handler.AllowAutoRedirect = true);

        handler.AllowAutoRedirect.ShouldBeFalse();
    }

    [Fact]
    public async Task ARedirectFromTheExchange_IsRejectedAndNotFollowed()
    {
        _http.Respond(HttpStatusCode.TemporaryRedirect, null, ("Location", "https://elsewhere.example.com/token"));

        var exception = await Should.ThrowAsync<StorytellerAuthenticationException>(async () => await Bind(Create()).GetAuthorizationAsync(null, CancellationToken.None));

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Rejected);
        exception.Message.ShouldContain("redirect");
        _http.Requests.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task An503FromTheExchange_IsUnavailable_AfterRetries()
    {
        _http.Otherwise = (_, _) => Task.FromResult(StubHttpHandler.Response(
            HttpStatusCode.ServiceUnavailable,
            """{ "error": "temporarily_unavailable", "error_description": "Try again later." }""",
            ("Retry-After", "0")));

        // The default resilience, the delay aside: the retries of one exchange must not open the circuit.
        var credential = Bind(Create());

        var exception = await Should.ThrowAsync<StorytellerAuthenticationException>(async () => await credential.GetAuthorizationAsync(null, CancellationToken.None));

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Unavailable);
        exception.Message.ShouldContain("503: temporarily_unavailable: Try again later");
        _http.Requests.Count.ShouldBe(4);
    }

    [Fact]
    public async Task AnOpenCircuit_IsUnavailable_AndNamesTheLastFailure()
    {
        _http.Otherwise = (_, _) => Task.FromResult(StubHttpHandler.Response(
            HttpStatusCode.ServiceUnavailable,
            """{ "error": "temporarily_unavailable", "error_description": "Try again later." }"""));
        var credential = Bind(Create());

        for (var call = 0; call < 3; call++)
        {
            await Should.ThrowAsync<StorytellerAuthenticationException>(async () => await credential.GetAuthorizationAsync(null, CancellationToken.None));
        }

        var exception = await Should.ThrowAsync<StorytellerAuthenticationException>(async () => await credential.GetAuthorizationAsync(null, CancellationToken.None));

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Unavailable);
        exception.InnerException.ShouldBeOfType<BrokenCircuitException>();
        exception.Message.ShouldContain("503: temporarily_unavailable: Try again later");
        _http.Requests.Count.ShouldBe(12);
    }

    [Fact]
    public async Task AMissingSubjectTokenFile_IsAConfigurationError()
    {
        _fileSystem.File.Delete(SubjectPath);

        var exception = await Should.ThrowAsync<StorytellerAuthenticationException>(async () => await Bind(Create()).GetAuthorizationAsync(null, CancellationToken.None));

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Configuration);
        exception.Message.ShouldContain(SubjectPath);
        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnUnboundCredential_BindsFromTheEnvironment()
    {
        _http.Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-1"));
        var variables = new Dictionary<string, string?>
        {
            ["STORYTELLER_CLIENT_ID"] = MachineId,
            ["STORYTELLER_TOKEN_FILE"] = SubjectPath,
            ["STORYTELLER_ENDPOINT"] = "https://env.example.com/api/",
            ["STORYTELLER_KEY"] = "@contoso.payments.default.sbt.northwind",
        };
        var credential = StorytellerCredential.FromEnvironment(name => variables.GetValueOrDefault(name), _fileSystem, _time)!.WithPrimaryHandler(_http);

        await credential.GetAuthorizationAsync(null, CancellationToken.None);

        _http.Requests.ShouldHaveSingleItem().Uri.ShouldBe(new Uri("https://env.example.com/api/v1/contoso/payments/access/token"));
    }

    [Fact]
    public async Task AnUnboundCredential_FallsBackToTheSformEndpointName()
    {
        _http.Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-1"));
        var variables = new Dictionary<string, string?>
        {
            ["STORYTELLER_CLIENT_ID"] = MachineId,
            ["STORYTELLER_SUBJECT_TOKEN_FILE"] = SubjectPath,
            ["STORYTELLER_API_URL"] = "http://localhost:7071/api",
            ["STORYTELLER_KEY"] = "@contoso.payments.default.sbt.northwind",
        };
        var credential = StorytellerCredential.FromEnvironment(name => variables.GetValueOrDefault(name), _fileSystem, _time)!.WithPrimaryHandler(_http);

        await credential.GetAuthorizationAsync(null, CancellationToken.None);

        _http.Requests.ShouldHaveSingleItem().Uri.ShouldBe(new Uri("http://localhost:7071/api/v1/contoso/payments/access/token"));
    }

    [Fact]
    public async Task AnUnboundCredential_WithoutAKey_IsAConfigurationError()
    {
        var variables = new Dictionary<string, string?>
        {
            ["STORYTELLER_CLIENT_ID"] = MachineId,
            ["STORYTELLER_TOKEN_FILE"] = SubjectPath,
            ["STORYTELLER_ENDPOINT"] = "https://env.example.com/api",
        };
        var credential = StorytellerCredential.FromEnvironment(name => variables.GetValueOrDefault(name), _fileSystem, _time)!.WithPrimaryHandler(_http);

        var exception = await Should.ThrowAsync<StorytellerAuthenticationException>(async () => await credential.GetAuthorizationAsync(null, CancellationToken.None));

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Configuration);
        exception.Message.ShouldContain("STORYTELLER_KEY");
        _http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Bind_OverridesTheEnvironment()
    {
        _http.Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-1"));
        var variables = new Dictionary<string, string?>
        {
            ["STORYTELLER_CLIENT_ID"] = MachineId,
            ["STORYTELLER_TOKEN_FILE"] = SubjectPath,
            ["STORYTELLER_ENDPOINT"] = "https://env.example.com/api",
            ["STORYTELLER_KEY"] = "@contoso.payments.default.sbt.northwind",
        };

        var credential = StorytellerCredential.FromEnvironment(name => variables.GetValueOrDefault(name), _fileSystem, _time)!
            .Bind(new StorytellerCredentialBinding(new Uri("https://api.example.com/api")) { Organization = "acme", Project = "billing" })
            .WithPrimaryHandler(_http);
        await credential.GetAuthorizationAsync(null, CancellationToken.None);

        _http.Requests.ShouldHaveSingleItem().Uri.ShouldBe(new Uri("https://api.example.com/api/v1/acme/billing/access/token"));
    }

    [Fact]
    public async Task TheOptions_WinOverTheBinding()
    {
        _http.Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-1"));
        var credential = Bind(Create(new FederatedTokenOptions
        {
            ClientId = MachineId,
            SubjectTokenPath = SubjectPath,
            FileSystem = _fileSystem,
            Endpoint = new Uri("https://own.example.com/api"),
            Project = "own-project",
        }));

        await credential.GetAuthorizationAsync(null, CancellationToken.None);

        _http.Requests.ShouldHaveSingleItem().Uri.ShouldBe(new Uri("https://own.example.com/api/v1/acme/own-project/access/token"));
    }

    [Theory]
    [InlineData("http://loopback/api")]
    [InlineData("http://api.example.com/api")]
    [InlineData("https://user:password@api.example.com/api")]
    [InlineData("https://api.example.com/api?password=1")]
    [InlineData("https://api.example.com/api#password")]
    public async Task AnEndpointThatBreaksTheLocalUrlRule_IsAConfigurationError(string endpoint)
    {
        var credential = Create().Bind(new StorytellerCredentialBinding(new Uri(endpoint)) { Organization = "acme", Project = "billing" }).WithPrimaryHandler(_http);

        var exception = await Should.ThrowAsync<StorytellerAuthenticationException>(async () => await credential.GetAuthorizationAsync(null, CancellationToken.None));

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Configuration);
        exception.Message.ShouldNotContain("password");
        _http.Requests.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("saml")]
    [InlineData("urn:ietf:params:oauth:token-type:saml2")]
    public void AnUnknownSubjectTokenType_IsAConfigurationError(string type)
    {
        var exception = Should.Throw<StorytellerAuthenticationException>(() => StorytellerCredential.FromFederatedTokenFile(
            new FederatedTokenOptions { ClientId = MachineId, SubjectTokenPath = SubjectPath, SubjectTokenType = type }));

        exception.Reason.ShouldBe(StorytellerAuthenticationFailureReason.Configuration);
    }

    [Fact]
    public async Task TheExchangedToken_IsCachedAndRenewedOnRejection()
    {
        _http.Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-1"));
        _http.Respond(HttpStatusCode.OK, TestTokens.TokenResponse("storyteller-2"));
        var credential = Bind(Create());

        var first = await credential.GetAuthorizationAsync(null, CancellationToken.None);
        (await credential.GetAuthorizationAsync(null, CancellationToken.None)).ShouldBe(first);
        var renewed = await credential.GetAuthorizationAsync(first, CancellationToken.None);

        renewed!.Parameter.ShouldBe("storyteller-2");
        credential.CanRenew.ShouldBeTrue();
        _http.Requests.Count.ShouldBe(2);
    }

    private StorytellerCredential Create(FederatedTokenOptions? options = null)
    {
        return StorytellerCredential.FromFederatedTokenFile(options ?? new FederatedTokenOptions
        {
            ClientId = MachineId,
            SubjectTokenPath = SubjectPath,
            FileSystem = _fileSystem,
        });
    }

    private StorytellerCredential Bind(StorytellerCredential credential, StorytellerResilienceOptions? resilience = null)
    {
        return credential
            .Bind(new StorytellerCredentialBinding(new Uri("https://api.example.com/api/"))
            {
                Organization = "acme",
                Project = "billing",
                TimeProvider = _time,
                Resilience = resilience ?? new StorytellerResilienceOptions { BaseDelay = TimeSpan.Zero },
            })
            .WithPrimaryHandler(_http);
    }
}
