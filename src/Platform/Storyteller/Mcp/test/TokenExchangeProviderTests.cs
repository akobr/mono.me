using System.Collections.Concurrent;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Security.Cryptography;
using System.Text;

using _42.Platform.Storyteller.Mcp.Authentication;
using _42.Platform.Storyteller.Mcp.Hosting;
using _42.Platform.Storyteller.Mcp.UnitTests.Support;

using Azure.Core;
using Azure.Identity;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Mcp.UnitTests;

// The client of the API's user token exchange, against a token endpoint of its own: the request per credential, the
// answer, the cache, the coalescing of concurrent exchanges and the mapping of every refusal.
public sealed class TokenExchangeProviderTests : IDisposable
{
    private const string ActorToken = "actor.workload.token";
    private const string Marker = "EXCHANGE-SECRET-MARKER-c41a";

    private readonly List<ExchangeFixture> _fixtures = [];

    public static TheoryData<string, Func<HttpResponseMessage>, Type, string> Refusals() => new()
    {
        {
            "no account",
            () => TokenEndpointHandler.Error("invalid_grant", StorytellerSignInRefusedException.NoAccountMessage),
            typeof(StorytellerSignInRefusedException),
            "No Storyteller account exists for this sign-in. Register once with sform or the admin UI."
        },
        {
            "subject token refused",
            () => TokenEndpointHandler.Error("invalid_grant", "The subject token is not accepted for this actor."),
            typeof(StorytellerSignInRefusedException),
            "The subject token is not accepted for this actor."
        },
        {
            "description with control characters",
            () => TokenEndpointHandler.Error("invalid_grant", "\\u0007Refused\\u0000 here\\u001b[31m\\r\\n"),
            typeof(StorytellerSignInRefusedException),
            "Refused here[31m"
        },
        {
            "invalid_grant without a description",
            () => TokenEndpointHandler.Error("invalid_grant"),
            typeof(StorytellerSignInRefusedException),
            StorytellerSignInRefusedException.DefaultMessage
        },
        {
            "actor not accepted",
            () => TokenEndpointHandler.Error("invalid_client", "The actor is not known or its proof is not valid."),
            typeof(StorytellerActorException),
            StorytellerActorException.ActorNotAcceptedMessage
        },
        {
            "actor not accepted with 401",
            () => TokenEndpointHandler.Error("invalid_client", status: HttpStatusCode.Unauthorized),
            typeof(StorytellerActorException),
            StorytellerActorException.ActorNotAcceptedMessage
        },
        {
            "exchange turned off",
            () => TokenEndpointHandler.Error("unsupported_grant_type", "The exchange of user tokens is not enabled."),
            typeof(StorytellerActorException),
            StorytellerActorException.ExchangeDisabledMessage
        },
        {
            "temporarily unavailable",
            () => TokenEndpointHandler.Error("temporarily_unavailable", "The keys to verify the request could not be retrieved.", HttpStatusCode.ServiceUnavailable),
            typeof(StorytellerSignInUnavailableException),
            StorytellerSignInUnavailableException.UnavailableMessage
        },
        {
            "gateway without a body",
            () => new HttpResponseMessage(HttpStatusCode.BadGateway),
            typeof(StorytellerSignInUnavailableException),
            StorytellerSignInUnavailableException.UnavailableMessage
        },
        {
            "invalid_scope",
            () => TokenEndpointHandler.Error("invalid_scope", "The requested scope is not granted to the actor; it must include User.Impersonation."),
            typeof(StorytellerSignInFailedException),
            "The MCP server could not get a token for the Storyteller API (invalid_scope)."
        },
        {
            "server error",
            () => TokenEndpointHandler.Json($$"""{"Message":"{{Marker}}"}""", HttpStatusCode.InternalServerError),
            typeof(StorytellerSignInFailedException),
            "The MCP server could not get a token for the Storyteller API (http_500)."
        },
        {
            "redirect, not followed",
            () => new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri("https://elsewhere.test/token") } },
            typeof(StorytellerSignInFailedException),
            "The MCP server could not get a token for the Storyteller API (http_302)."
        },
        {
            "error code that is no code",
            () => TokenEndpointHandler.Json($$"""{"error":"{{Marker}} <script>"}""", HttpStatusCode.BadRequest),
            typeof(StorytellerSignInFailedException),
            "The MCP server could not get a token for the Storyteller API (http_400)."
        },
        {
            "answer that is not JSON",
            () => TokenEndpointHandler.Json("<html>Bad gateway</html>"),
            typeof(StorytellerSignInFailedException),
            "The MCP server could not get a token for the Storyteller API (invalid_response)."
        },
        {
            "answer without a token",
            () => TokenEndpointHandler.Json("""{"token_type":"Bearer","expires_in":600}"""),
            typeof(StorytellerSignInFailedException),
            "The MCP server could not get a token for the Storyteller API (invalid_response)."
        },
        {
            "answer without a lifetime",
            () => TokenEndpointHandler.Json("""{"access_token":"t","token_type":"Bearer"}"""),
            typeof(StorytellerSignInFailedException),
            "The MCP server could not get a token for the Storyteller API (invalid_response)."
        },
        {
            "answer of another token type",
            () => TokenEndpointHandler.Json("""{"access_token":"t","token_type":"N_A","expires_in":600}"""),
            typeof(StorytellerSignInFailedException),
            "The MCP server could not get a token for the Storyteller API (invalid_response)."
        },
        {
            "malformed restrictions",
            () => TokenEndpointHandler.Json("""{"access_token":"t","token_type":"Bearer","expires_in":600,"restrictions":"NoSecretReveal"}"""),
            typeof(StorytellerSignInFailedException),
            "The MCP server could not get a token for the Storyteller API (invalid_response)."
        },
        {
            "answer larger than 64 KiB",
            () => TokenEndpointHandler.Json($$"""{"access_token":"{{new string('a', StorytellerTokenExchangeProvider.MaxResponseBytes)}}","expires_in":600}"""),
            typeof(StorytellerSignInFailedException),
            "The MCP server could not get a token for the Storyteller API (invalid_response)."
        },
    };

    public static TheoryData<string, Exception> TransportFailures() => new()
    {
        { "connection", new HttpRequestException($"Connection reset ({Marker}).") },
        { "timeout", new TaskCanceledException($"Timed out ({Marker}).", new TimeoutException()) },
        { "broken body", new IOException($"Broken ({Marker}).") },
    };

    [Fact]
    public async Task TokenFile_PostsTheExchangeFormWithTheActorToken()
    {
        var fixture = Fixture();
        fixture.Files.AddFile(ExchangeFixture.TokenFilePath, new MockFileData($"{ActorToken}\n"));
        var userToken = TestTokens.Create("ada");

        await fixture.Provider.GetTokenAsync(userToken, CancellationToken.None);

        var request = fixture.Endpoint.Requests.ShouldHaveSingleItem();
        request.Method.ShouldBe(HttpMethod.Post);
        request.Uri.ToString().ShouldBe(ExchangeFixture.TokenEndpoint);
        request.ContentType.ShouldBe("application/x-www-form-urlencoded");
        request.Authorization.ShouldBeNull();
        request.Form.Keys.Order().ShouldBe(["actor_token", "actor_token_type", "client_id", "grant_type", "subject_token", "subject_token_type"]);
        request.Field("grant_type").ShouldBe("urn:ietf:params:oauth:grant-type:token-exchange");
        request.Field("subject_token").ShouldBe(userToken);
        request.Field("subject_token_type").ShouldBe("urn:ietf:params:oauth:token-type:access_token");
        request.Field("client_id").ShouldBe(ExchangeFixture.ActorId);
        request.Field("actor_token").ShouldBe(ActorToken);
        request.Field("actor_token_type").ShouldBe("urn:ietf:params:oauth:token-type:jwt");
    }

    // The platform rotates the file, so every exchange reads it again.
    [Fact]
    public async Task TokenFile_IsReadForEveryExchange()
    {
        var fixture = Fixture();
        fixture.Files.AddFile(ExchangeFixture.TokenFilePath, new MockFileData("actor-token-1"));
        await fixture.Provider.GetTokenAsync(TestTokens.Create("ada"), CancellationToken.None);

        fixture.Files.File.WriteAllText(ExchangeFixture.TokenFilePath, "actor-token-2");
        await fixture.Provider.GetTokenAsync(TestTokens.Create("bob"), CancellationToken.None);

        fixture.Endpoint.Requests.Select(request => request.Field("actor_token")).ShouldBe(["actor-token-1", "actor-token-2"]);
    }

    [Theory]
    [InlineData("missing file", null)]
    [InlineData("empty file", " \n")]
    public async Task TokenFileThatHasNoToken_IsTheActorCredentialError_WithoutACall(string because, string? content)
    {
        var fixture = Fixture();

        if (content is not null)
        {
            fixture.Files.AddFile(ExchangeFixture.TokenFilePath, new MockFileData(content));
        }

        var exception = await Should.ThrowAsync<StorytellerActorException>(() => fixture.Provider.GetTokenAsync(TestTokens.Create(), CancellationToken.None));

        exception.Message.ShouldBe(StorytellerActorException.CredentialUnavailableMessage, because);
        fixture.Endpoint.Requests.ShouldBeEmpty(because);
    }

    [Fact]
    public async Task ManagedIdentity_AsksForTheAudienceAndSendsItsToken()
    {
        var identity = new FakeManagedIdentity(ActorToken);
        var fixture = Fixture(ExchangeFixture.ManagedIdentityActor("api://storyteller-token-exchange"), identity);

        await fixture.Provider.GetTokenAsync(TestTokens.Create(), CancellationToken.None);

        identity.Scopes.ShouldHaveSingleItem().ShouldBe(["api://storyteller-token-exchange/.default"]);
        var request = fixture.Endpoint.Requests.ShouldHaveSingleItem();
        request.Field("actor_token").ShouldBe(ActorToken);
        request.Field("actor_token_type").ShouldBe("urn:ietf:params:oauth:token-type:jwt");
        request.Form.ShouldNotContainKey("client_assertion");
        request.Form.ShouldNotContainKey("client_secret");
        request.Authorization.ShouldBeNull();
    }

    [Fact]
    public async Task ManagedIdentityThatFails_IsTheActorCredentialError_WithoutACall()
    {
        var identity = new FakeManagedIdentity(ActorToken) { Failure = new CredentialUnavailableException($"No managed identity endpoint ({Marker}).") };
        var fixture = Fixture(ExchangeFixture.ManagedIdentityActor(), identity);

        var exception = await Should.ThrowAsync<StorytellerActorException>(() => fixture.Provider.GetTokenAsync(TestTokens.Create(), CancellationToken.None));

        exception.Message.ShouldBe(StorytellerActorException.CredentialUnavailableMessage);
        fixture.Endpoint.Requests.ShouldBeEmpty();
        fixture.Logs.Entries.ShouldContain(entry => entry.Contains("The workload identity token of Mcp:Actor is not available (CredentialUnavailableException).", StringComparison.Ordinal));
        fixture.Logs.Entries.ShouldNotContain(entry => entry.Contains(Marker, StringComparison.Ordinal));
    }

    [Fact]
    public async Task ClientAssertionKey_SignsAnAssertionForTheTokenEndpoint()
    {
        var fixture = Fixture(ExchangeFixture.ClientAssertionActor());
        fixture.Files.AddFile(ExchangeFixture.CertificatePath, new MockFileData(TestCertificates.RsaPkcs12));

        await fixture.Provider.GetTokenAsync(TestTokens.Create("ada"), CancellationToken.None);
        await fixture.Provider.GetTokenAsync(TestTokens.Create("bob"), CancellationToken.None);

        var requests = fixture.Endpoint.Requests.ToList();
        requests.Count.ShouldBe(2);
        requests[0].Authorization.ShouldBeNull();
        requests[0].Form.Keys.Order().ShouldBe(["client_assertion", "client_assertion_type", "client_id", "grant_type", "subject_token", "subject_token_type"]);
        requests[0].Field("client_assertion_type").ShouldBe("urn:ietf:params:oauth:client-assertion-type:jwt-bearer");

        // Signed with the certificate's key, checked with its public key alone.
        var validation = await new JsonWebTokenHandler().ValidateTokenAsync(requests[0].Field("client_assertion"), new TokenValidationParameters
        {
            IssuerSigningKey = new RsaSecurityKey(TestCertificates.RsaPublicKey),
            ValidIssuer = ExchangeFixture.ActorId,
            ValidAudience = ExchangeFixture.TokenEndpoint,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidateLifetime = false,
            RequireSignedTokens = true,
        });
        validation.IsValid.ShouldBeTrue(validation.Exception?.Message);

        var assertion = (JsonWebToken)validation.SecurityToken;
        assertion.Alg.ShouldBe("RS256");
        assertion.Kid.ShouldBe(Rfc7638Thumbprint(TestCertificates.RsaPublicKey));
        assertion.Subject.ShouldBe(ExchangeFixture.ActorId);
        assertion.Audiences.ShouldBe([ExchangeFixture.TokenEndpoint]);
        assertion.IssuedAt.ShouldBe(fixture.Clock.Now.UtcDateTime);
        assertion.ValidTo.ShouldBe(fixture.Clock.Now.UtcDateTime + McpActorKey.ClientAssertionLifetime);
        (assertion.ValidTo - assertion.IssuedAt).ShouldBeLessThanOrEqualTo(TimeSpan.FromMinutes(5));

        // A new jti for every assertion: the API refuses one it has seen.
        var otherAssertion = new JsonWebToken(requests[1].Field("client_assertion"));
        assertion.Id.ShouldNotBeNullOrEmpty();
        otherAssertion.Id.ShouldNotBe(assertion.Id);
    }

    public static TheoryData<string, Func<byte[]>, string?> UnusableKeys() => new()
    {
        { "missing file", () => [], TestCertificates.Password },
        { "wrong password", () => TestCertificates.RsaPkcs12, "not-the-password" },
        { "elliptic curve key", () => TestCertificates.EllipticCurvePkcs12, TestCertificates.Password },
        { "RSA key below 2048 bits", () => TestCertificates.SmallRsaPkcs12, TestCertificates.Password },
    };

    [Theory]
    [MemberData(nameof(UnusableKeys))]
    public async Task ClientAssertionKeyThatCannotSign_IsTheActorCredentialError_WithoutACall(string because, Func<byte[]> pkcs12, string? password)
    {
        var fixture = Fixture(ExchangeFixture.ClientAssertionActor(password));
        var content = pkcs12();

        if (content.Length > 0)
        {
            fixture.Files.AddFile(ExchangeFixture.CertificatePath, new MockFileData(content));
        }

        var exception = await Should.ThrowAsync<StorytellerActorException>(() => fixture.Provider.GetTokenAsync(TestTokens.Create(), CancellationToken.None));

        exception.Message.ShouldBe(StorytellerActorException.CredentialUnavailableMessage, because);
        fixture.Endpoint.Requests.ShouldBeEmpty(because);
    }

    // The API knows itself by its public URL, which may not be the one the server reaches it by.
    [Fact]
    public async Task ClientAssertionKeyWithAnAudience_SignsTheAssertionForIt_AndPostsToTheBaseUrl()
    {
        const string publicTokenEndpoint = "https://storyteller.example.com/api/v1/access/token";
        var fixture = Fixture(ExchangeFixture.ClientAssertionActor(audience: publicTokenEndpoint));
        fixture.Files.AddFile(ExchangeFixture.CertificatePath, new MockFileData(TestCertificates.RsaPkcs12));

        await fixture.Provider.GetTokenAsync(TestTokens.Create(), CancellationToken.None);

        var request = fixture.Endpoint.Requests.ShouldHaveSingleItem();
        request.Uri.ToString().ShouldBe(ExchangeFixture.TokenEndpoint);
        new JsonWebToken(request.Field("client_assertion")).Audiences.ShouldBe([publicTokenEndpoint]);
    }

    // A key that cannot be read is read again RetryDelay later at the earliest, not for every exchange in between.
    [Fact]
    public async Task UnreadableKey_IsNotReadAgainWithinTheRetryDelay()
    {
        const string unreadable = "The client assertion key of Mcp:Actor could not be read";
        var fixture = Fixture(ExchangeFixture.ClientAssertionActor());

        var first = await Should.ThrowAsync<StorytellerActorException>(() => fixture.Provider.GetTokenAsync(TestTokens.Create("ada"), CancellationToken.None));
        first.Message.ShouldBe(StorytellerActorException.CredentialUnavailableMessage);

        fixture.Files.AddFile(ExchangeFixture.CertificatePath, new MockFileData(TestCertificates.RsaPkcs12));
        var second = await Should.ThrowAsync<StorytellerActorException>(() => fixture.Provider.GetTokenAsync(TestTokens.Create("bob"), CancellationToken.None));
        second.Message.ShouldBe(StorytellerActorException.CredentialUnavailableMessage);
        fixture.Endpoint.Requests.ShouldBeEmpty();
        fixture.Logs.Entries.Count(entry => entry.Contains(unreadable, StringComparison.Ordinal)).ShouldBe(1);

        fixture.Clock.Advance(McpActorKey.RetryDelay);
        await fixture.Provider.GetTokenAsync(TestTokens.Create("carol"), CancellationToken.None);
        fixture.Endpoint.Requests.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ClientSecret_IsSentAsClientSecret()
    {
        var fixture = Fixture(ExchangeFixture.ClientSecretActor("local-secret"));

        await fixture.Provider.GetTokenAsync(TestTokens.Create(), CancellationToken.None);

        var request = fixture.Endpoint.Requests.ShouldHaveSingleItem();
        request.Form.Keys.Order().ShouldBe(["client_id", "client_secret", "grant_type", "subject_token", "subject_token_type"]);
        request.Field("client_secret").ShouldBe("local-secret");
        request.Authorization.ShouldBeNull();
    }

    [Fact]
    public async Task Answer_IsTheTokenItsLifetimeScopeAndRestrictions()
    {
        var fixture = TokenFileFixture();
        fixture.Endpoint.Respond = _ => Task.FromResult(TokenEndpointHandler.Token("storyteller-token", 540, """["NoSecretReveal"]"""));

        var token = await fixture.Provider.GetTokenAsync(TestTokens.Create(), CancellationToken.None);

        token.AccessToken.ShouldBe("storyteller-token");
        token.ExpiresAt.ShouldBe(fixture.Clock.Now.AddSeconds(540));
        token.Scope.ShouldBe("User.Impersonation");
        token.Restrictions.ShouldBe(["NoSecretReveal"]);
        token.ToString().ShouldNotContain("storyteller-token");
    }

    [Fact]
    public async Task AnswerWithoutRestrictions_HasNone()
    {
        var fixture = TokenFileFixture();
        fixture.Endpoint.Respond = _ => Task.FromResult(TokenEndpointHandler.Token("storyteller-token", restrictions: "[]"));

        var token = await fixture.Provider.GetTokenAsync(TestTokens.Create(), CancellationToken.None);

        token.Restrictions.ShouldBeEmpty();
    }

    [Fact]
    public async Task Answer_IsCachedUntilAMinuteBeforeItExpires()
    {
        var fixture = TokenFileFixture();
        var userToken = TestTokens.Create();
        fixture.Endpoint.Respond = request => Task.FromResult(TokenEndpointHandler.Token($"token-{fixture.Endpoint.Requests.Count}", 600));

        var first = await fixture.Provider.GetTokenAsync(userToken, CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromSeconds(600 - 61));
        var cached = await fixture.Provider.GetTokenAsync(userToken, CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromSeconds(2));
        var refreshed = await fixture.Provider.GetTokenAsync(userToken, CancellationToken.None);

        cached.ShouldBeSameAs(first);
        refreshed.AccessToken.ShouldBe("token-2");
        fixture.Endpoint.Requests.Count.ShouldBe(2);
    }

    // The answer cannot outlive the subject token, so a new exchange would not live longer: it is kept until
    // MinRemainingLifetime before it expires.
    [Fact]
    public async Task AnswerThatExpiresWithinAMinute_IsCachedUntilShortlyBeforeItExpires()
    {
        var fixture = TokenFileFixture();
        var userToken = TestTokens.Create();
        fixture.Endpoint.Respond = _ => Task.FromResult(TokenEndpointHandler.Token("short", 60));

        await fixture.Provider.GetTokenAsync(userToken, CancellationToken.None);
        await fixture.Provider.GetTokenAsync(userToken, CancellationToken.None);
        await fixture.Provider.GetTokenAsync(userToken, CancellationToken.None);
        fixture.Endpoint.Requests.Count.ShouldBe(1);

        fixture.Clock.Advance(TimeSpan.FromSeconds(56));
        await fixture.Provider.GetTokenAsync(userToken, CancellationToken.None);
        fixture.Endpoint.Requests.Count.ShouldBe(2);
    }

    // In the last minute of the subject token the answers are short; the calls of that minute still share one exchange,
    // within the API's replay limit of 10 a minute per subject token.
    [Fact]
    public async Task FinalMinuteOfAToken_CostsOneExchange()
    {
        var fixture = TokenFileFixture();
        var userToken = TestTokens.Create();
        fixture.Endpoint.Respond = _ => Task.FromResult(TokenEndpointHandler.Token($"token-{fixture.Endpoint.Requests.Count}", fixture.Endpoint.Requests.Count == 1 ? 300 : 59));

        await fixture.Provider.GetTokenAsync(userToken, CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromSeconds(241));

        for (var call = 0; call < 12; call++)
        {
            (await fixture.Provider.GetTokenAsync(userToken, CancellationToken.None)).AccessToken.ShouldBe("token-2");
        }

        fixture.Endpoint.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task AnswerThatExpiresWithinFiveSeconds_IsNotCached()
    {
        var fixture = TokenFileFixture();
        var userToken = TestTokens.Create();
        fixture.Endpoint.Respond = _ => Task.FromResult(TokenEndpointHandler.Token("almost-gone", 5));

        await fixture.Provider.GetTokenAsync(userToken, CancellationToken.None);
        await fixture.Provider.GetTokenAsync(userToken, CancellationToken.None);

        fixture.Endpoint.Requests.Count.ShouldBe(2);
    }

    // The cache is bounded by CacheSizeLimit: a full cache takes no more answers, and each call for them exchanges again.
    [Fact]
    public async Task CacheThatIsFull_CachesNoMoreAnswers()
    {
        var fixture = TokenFileFixture();
        using var provider = new StorytellerTokenExchangeProvider(
            fixture.Services.GetRequiredService<IHttpClientFactory>(),
            fixture.Services.GetRequiredService<McpActorCredential>(),
            Options.Create(new StorytellerApiOptions { BaseUrl = ExchangeFixture.BaseUrl }),
            fixture.Clock,
            fixture.Services.GetRequiredService<ILogger<StorytellerTokenExchangeProvider>>(),
            StorytellerTokenExchangeProvider.ExchangeTimeout,
            cacheSizeLimit: 1);
        fixture.Endpoint.Respond = _ => Task.FromResult(TokenEndpointHandler.Token($"token-{fixture.Endpoint.Requests.Count}", 600));
        var adaToken = TestTokens.Create("ada");
        var bobToken = TestTokens.Create("bob");

        var ada = await provider.GetTokenAsync(adaToken, CancellationToken.None);
        (await provider.GetTokenAsync(adaToken, CancellationToken.None)).ShouldBeSameAs(ada);
        fixture.Endpoint.Requests.Count.ShouldBe(1);

        await provider.GetTokenAsync(bobToken, CancellationToken.None);
        await provider.GetTokenAsync(bobToken, CancellationToken.None);
        fixture.Endpoint.Requests.Count.ShouldBe(3);
    }

    [Fact]
    public async Task ConcurrentCallsForOneToken_ShareOneExchange()
    {
        var fixture = TokenFileFixture();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Endpoint.Respond = async _ =>
        {
            await release.Task;
            return TokenEndpointHandler.Token("shared");
        };
        var userToken = TestTokens.Create();

        var calls = Enumerable.Range(0, 10).Select(_ => fixture.Provider.GetTokenAsync(userToken, CancellationToken.None)).ToList();
        release.SetResult();
        var tokens = await Task.WhenAll(calls);

        fixture.Endpoint.Requests.Count.ShouldBe(1);
        tokens.ShouldAllBe(token => ReferenceEquals(token, tokens[0]));
        tokens[0].AccessToken.ShouldBe("shared");
    }

    [Fact]
    public async Task TwoPeople_EachGetTheirOwnToken()
    {
        var fixture = TokenFileFixture();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Endpoint.Respond = async request =>
        {
            await release.Task;
            return TokenEndpointHandler.Token($"for-{new JsonWebToken(request.Field("subject_token")).Subject}");
        };
        var adaToken = TestTokens.Create("ada");
        var bobToken = TestTokens.Create("bob");

        var calls = new[] { adaToken, bobToken, adaToken, bobToken }.Select(token => fixture.Provider.GetTokenAsync(token, CancellationToken.None)).ToList();
        release.SetResult();
        var tokens = await Task.WhenAll(calls);
        var again = await Task.WhenAll(fixture.Provider.GetTokenAsync(bobToken, CancellationToken.None), fixture.Provider.GetTokenAsync(adaToken, CancellationToken.None));

        tokens.Select(token => token.AccessToken).ShouldBe(["for-ada", "for-bob", "for-ada", "for-bob"]);
        again.Select(token => token.AccessToken).ShouldBe(["for-bob", "for-ada"]);
        fixture.Endpoint.Requests.Select(request => request.Field("subject_token")).Order().ShouldBe(new[] { adaToken, bobToken }.Order());
    }

    // A caller that stops waiting leaves the exchange to the others, and its answer to the cache.
    [Fact]
    public async Task CancelledCaller_DoesNotCancelTheSharedExchange()
    {
        var fixture = TokenFileFixture();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Endpoint.Respond = async _ =>
        {
            await release.Task;
            return TokenEndpointHandler.Token("shared");
        };
        var userToken = TestTokens.Create();
        using var cancellation = new CancellationTokenSource();

        var cancelled = fixture.Provider.GetTokenAsync(userToken, cancellation.Token);
        var waiting = fixture.Provider.GetTokenAsync(userToken, CancellationToken.None);
        await cancellation.CancelAsync();
        await Should.ThrowAsync<OperationCanceledException>(() => cancelled);
        release.SetResult();

        (await waiting).AccessToken.ShouldBe("shared");
        (await fixture.Provider.GetTokenAsync(userToken, CancellationToken.None)).AccessToken.ShouldBe("shared");
        fixture.Endpoint.Requests.Count.ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task Refusal_IsTheSignInErrorWithItsText(string because, Func<HttpResponseMessage> answer, Type expected, string message)
    {
        var fixture = TokenFileFixture();
        fixture.Endpoint.Respond = _ => Task.FromResult(answer());

        var exception = await Should.ThrowAsync<StorytellerSignInException>(() => fixture.Provider.GetTokenAsync(TestTokens.Create(), CancellationToken.None));

        exception.GetType().ShouldBe(expected, because);
        exception.Message.ShouldBe(message, because);
        exception.Message.ShouldNotContain(Marker, Case.Sensitive, because);
        fixture.Endpoint.Requests.Count.ShouldBe(1, because);
    }

    // Cleaned before it is cut: the control character in front does not count against the length.
    [Fact]
    public async Task LongDescription_IsCut()
    {
        var fixture = TokenFileFixture();
        fixture.Endpoint.Respond = _ => Task.FromResult(TokenEndpointHandler.Error("invalid_grant", "\\u0007" + new string('x', 1000)));

        var exception = await Should.ThrowAsync<StorytellerSignInRefusedException>(() => fixture.Provider.GetTokenAsync(TestTokens.Create(), CancellationToken.None));

        exception.Message.ShouldBe(new string('x', StorytellerTokenExchangeProvider.MaxDescriptionLength));
    }

    [Theory]
    [MemberData(nameof(TransportFailures))]
    public async Task TransportFailure_IsTheUnavailableSignIn(string because, Exception failure)
    {
        var fixture = TokenFileFixture();
        fixture.Endpoint.Respond = _ => throw failure;

        var exception = await Should.ThrowAsync<StorytellerSignInUnavailableException>(() => fixture.Provider.GetTokenAsync(TestTokens.Create(), CancellationToken.None));

        exception.Message.ShouldBe(StorytellerSignInUnavailableException.UnavailableMessage, because);

        // The exception type, never the exception (the HttpClient's own log of a failed request is the framework's).
        var logged = fixture.Logs.Entries.Where(entry => entry.Contains($" {typeof(StorytellerTokenExchangeProvider).FullName}: ", StringComparison.Ordinal)).ToList();
        logged.ShouldContain(entry => entry.Contains($"The token exchange with the Storyteller API did not finish ({failure.GetType().Name}).", StringComparison.Ordinal), because);
        logged.ShouldNotContain(entry => entry.Contains(Marker, StringComparison.Ordinal), because);
    }

    // Every attempt counts against the API's replay limit per subject token: the defaults' retries are gone.
    [Theory]
    [InlineData("503")]
    [InlineData("connection")]
    public async Task Failure_IsNotRetried(string failure)
    {
        var fixture = TokenFileFixture();
        fixture.Endpoint.Respond = _ => failure == "503"
            ? Task.FromResult(TokenEndpointHandler.Error("temporarily_unavailable", status: HttpStatusCode.ServiceUnavailable))
            : throw new HttpRequestException("Connection refused.");

        await Should.ThrowAsync<StorytellerSignInUnavailableException>(() => fixture.Provider.GetTokenAsync(TestTokens.Create(), CancellationToken.None));

        fixture.Endpoint.Requests.Count.ShouldBe(1);
    }

    // HttpClient.Timeout is infinite: the exchange's own timeout ends an answer that never comes, and frees the subject
    // token for the next exchange.
    [Fact]
    public async Task HungExchange_TimesOut_AndTheNextCallStartsANewOne()
    {
        var fixture = TokenFileFixture(exchangeTimeout: TimeSpan.FromMilliseconds(200));
        var userToken = TestTokens.Create();
        fixture.Endpoint.Respond = _ => fixture.Endpoint.Requests.Count == 1
            ? new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously).Task
            : Task.FromResult(TokenEndpointHandler.Token("after-the-timeout"));

        // A bounded wait, so a lost timeout fails the test instead of hanging it.
        await Should.ThrowAsync<StorytellerSignInUnavailableException>(() => fixture.Provider.GetTokenAsync(userToken, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)));
        fixture.Endpoint.Requests.Count.ShouldBe(1);
        fixture.Logs.Entries.ShouldContain(entry => entry.Contains("The token exchange with the Storyteller API did not finish (", StringComparison.Ordinal));

        var token = await fixture.Provider.GetTokenAsync(userToken, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        token.AccessToken.ShouldBe("after-the-timeout");
        fixture.Endpoint.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Failure_IsNotCached()
    {
        var fixture = TokenFileFixture();
        var userToken = TestTokens.Create();
        fixture.Endpoint.Respond = _ => Task.FromResult(fixture.Endpoint.Requests.Count == 1
            ? TokenEndpointHandler.Error("temporarily_unavailable", status: HttpStatusCode.ServiceUnavailable)
            : TokenEndpointHandler.Token("after-the-failure"));

        await Should.ThrowAsync<StorytellerSignInUnavailableException>(() => fixture.Provider.GetTokenAsync(userToken, CancellationToken.None));
        var token = await fixture.Provider.GetTokenAsync(userToken, CancellationToken.None);

        token.AccessToken.ShouldBe("after-the-failure");
        fixture.Endpoint.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public void Client_HasNoResilienceHandler_AndFollowsNoRedirect()
    {
        var fixture = Fixture(endpoint: false);
        var factory = fixture.Services.GetRequiredService<IHttpMessageHandlerFactory>();

        var exchange = Chain(factory.CreateHandler(StorytellerTokenExchangeProvider.HttpClientName));
        var other = Chain(factory.CreateHandler("another-client"));

        // The fixture adds the defaults' handler to every client, as Aspire.ServiceDefaults does.
        other.ShouldContain(handler => handler is ResilienceHandler);
        exchange.ShouldNotContain(handler => handler is ResilienceHandler);
        exchange.OfType<SocketsHttpHandler>().ShouldHaveSingleItem().AllowAutoRedirect.ShouldBeFalse();
    }

    [Fact]
    public async Task Logs_CarryNoTokenAndNoBody()
    {
        var fixture = Fixture();
        fixture.Files.AddFile(ExchangeFixture.TokenFilePath, new MockFileData($"actor-{Marker}"));
        var userToken = TestTokens.Create(subject: $"user-{Marker}");
        fixture.Endpoint.Respond = request => Task.FromResult(request.Field("subject_token") == userToken
            ? TokenEndpointHandler.Token($"storyteller-{Marker}", 60)
            : TokenEndpointHandler.Error("invalid_grant", $"Refused {Marker}"));

        await fixture.Provider.GetTokenAsync(userToken, CancellationToken.None);
        await Should.ThrowAsync<StorytellerSignInRefusedException>(() => fixture.Provider.GetTokenAsync(TestTokens.Create(), CancellationToken.None));

        fixture.Logs.Entries.ShouldContain(entry => entry.Contains("The Storyteller API refused the token exchange: 400 invalid_grant.", StringComparison.Ordinal));
        fixture.Logs.Entries.ShouldNotContain(entry => entry.Contains(Marker, StringComparison.Ordinal));
        fixture.Logs.Entries.ShouldNotContain(entry => entry.Contains(userToken, StringComparison.Ordinal));
    }

    // A refusal for every person usually means the API's actor lacks an audience this server accepts; a missing account
    // does not.
    [Theory]
    [InlineData("The subject token is not accepted for this actor.", true)]
    [InlineData(null, true)]
    [InlineData(StorytellerSignInRefusedException.NoAccountMessage, false)]
    public async Task RefusedSignIn_HintsAtTheActorsSubjectAudiences_UnlessTheAccountIsMissing(string? description, bool hinted)
    {
        var fixture = TokenFileFixture();
        fixture.Endpoint.Respond = _ => Task.FromResult(TokenEndpointHandler.Error("invalid_grant", description));

        await Should.ThrowAsync<StorytellerSignInRefusedException>(() => fixture.Provider.GetTokenAsync(TestTokens.Create(), CancellationToken.None));

        var logged = fixture.Logs.Entries.Where(entry => entry.Contains("The Storyteller API refused the token exchange: 400 invalid_grant.", StringComparison.Ordinal)).ToList();
        logged.ShouldHaveSingleItem();
        logged[0].Contains($"Auth:TokenExchange:Actors entry '{ExchangeFixture.ActorId}' lists in SubjectAudiences", StringComparison.Ordinal).ShouldBe(hinted);
    }

    public void Dispose()
    {
        foreach (var fixture in _fixtures)
        {
            fixture.Dispose();
        }
    }

    // RFC 7638 section 3.2: the SHA-256 of {"e","kty","n"} in this order, without whitespace.
    private static string Rfc7638Thumbprint(RSAParameters key)
    {
        var json = $$"""{"e":"{{Base64UrlEncoder.Encode(key.Exponent)}}","kty":"RSA","n":"{{Base64UrlEncoder.Encode(key.Modulus)}}"}""";
        return Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static List<HttpMessageHandler> Chain(HttpMessageHandler handler)
    {
        var chain = new List<HttpMessageHandler>();

        for (HttpMessageHandler? current = handler; current is not null; current = (current as DelegatingHandler)?.InnerHandler)
        {
            chain.Add(current);
        }

        return chain;
    }

    private ExchangeFixture Fixture(McpActorOptions? actor = null, TokenCredential? managedIdentity = null, bool endpoint = true, TimeSpan? exchangeTimeout = null)
    {
        var fixture = new ExchangeFixture(actor, managedIdentity, endpoint, exchangeTimeout);
        _fixtures.Add(fixture);
        return fixture;
    }

    private ExchangeFixture TokenFileFixture(TimeSpan? exchangeTimeout = null)
    {
        var fixture = Fixture(exchangeTimeout: exchangeTimeout);
        fixture.Files.AddFile(ExchangeFixture.TokenFilePath, new MockFileData(ActorToken));
        return fixture;
    }

    private sealed class FakeManagedIdentity(string token) : TokenCredential
    {
        public ConcurrentQueue<string[]> Scopes { get; } = new();

        public Exception? Failure { get; init; }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            throw new NotSupportedException("The exchange asks asynchronously.");
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            Scopes.Enqueue(requestContext.Scopes);

            return Failure is null
                ? ValueTask.FromResult(new AccessToken(token, DateTimeOffset.UtcNow.AddHours(1)))
                : ValueTask.FromException<AccessToken>(Failure);
        }
    }
}
