using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;

namespace _42.Platform.Storyteller.Access.Federation.UnitTests;

public sealed class UserTokenExchangeServiceTests : IDisposable
{
    private const string ActorId = "mcp";
    private const string AccountId = "user_01HZX3Y4QJ7K9M2N";
    private const string ClientId = "client_01HZX3Y4";
    private const string McpResource = "https://mcp.example/mcp";
    private const string StorytellerIssuer = "https://storyteller.example";
    private const string StorytellerAudience = "https://storyteller.example/api";
    private const string TokenEndpoint = "https://storyteller.example/api/v1/access/token";
    private const string ActorJwksUri = "https://mcp.example/.well-known/jwks.json";
    private const string Secret = "a local development secret";
    private const string SubjectToken = "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiJ1c2VyIn0.c2lnbmF0dXJl";

    private readonly ManualTimeProvider _time = new();
    private readonly StubHttpMessageHandler _http = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly TestSigningKey _actorKey = TestSigningKey.Rsa("mcp-1");
    private readonly CountingSigningKeyStore _signingKeys = new();
    private readonly Mock<IAccessService> _access = new();
    private readonly FakeUserSubjectTokenValidator _subjects = new();
    private readonly RecordingLogger<UserTokenExchangeService> _logger = new();
    private readonly FederationOptions _options = new() { Issuer = StorytellerIssuer, Audience = StorytellerAudience };
    private readonly TokenExchangeOptions _exchangeOptions = new() { Enabled = true };
    private readonly TestHostEnvironment _environment = new("Development");
    private readonly TokenSigningKeyProvider _keyProvider;
    private readonly ActorAuthenticator _authenticator;
    private readonly UserTokenExchangeService _exchange;

    public UserTokenExchangeServiceTests()
    {
        var options = Options.Create(_options);
        var resolver = new FederatedIssuerKeyResolver(new StubHttpClientFactory(_http), _cache, options, _time, NullLogger<FederatedIssuerKeyResolver>.Instance);
        _keyProvider = new TokenSigningKeyProvider(_signingKeys, options, _time, NullLogger<TokenSigningKeyProvider>.Instance);
        _authenticator = new ActorAuthenticator(resolver, Options.Create(_exchangeOptions), options, _environment, _time);
        _exchange = new UserTokenExchangeService(
            _authenticator,
            _subjects,
            _access.Object,
            new StorytellerTokenIssuer(_keyProvider, options, _time),
            Options.Create(_exchangeOptions),
            options,
            _time,
            _logger);

        _exchangeOptions.Actors = [SecretActor(ActorId)];
        _subjects.Result = Subject();
        _http.SetJson(ActorJwksUri, TestSigningKey.Jwks(_actorKey));
        _access.Setup(service => service.GetAccountAsync(AccountId)).ReturnsAsync(new Account
        {
            Id = AccountId,
            UserName = "ada@example.com",
            Name = "Ada Lovelace",
            AccessMap = new Dictionary<string, AccountRole>(),
        });
    }

    public void Dispose()
    {
        _exchange.Dispose();
        _authenticator.Dispose();
        _actorKey.Dispose();
        _http.Dispose();
        _cache.Dispose();
    }

    [Fact]
    public async Task ValidExchange_IssuesAUserTokenOfTheAccountThatNamesTheActor()
    {
        var result = await _exchange.ExchangeAsync(Request());

        result.Succeeded.ShouldBeTrue();
        result.IssuedTokenType.ShouldBe(OAuthTokenExchange.TokenTypes.AccessToken);
        result.ExpiresIn.ShouldBe(600);
        result.Scope.ShouldBe(UserScopes.Impersonation);
        result.Restrictions.ShouldBe([ActorRestrictions.NoSecretReveal]);

        var token = new JsonWebToken(result.AccessToken);
        token.Typ.ShouldBe("at+jwt");
        token.Alg.ShouldBe("RS256");
        token.Issuer.ShouldBe(StorytellerIssuer);
        token.Audiences.ShouldBe([StorytellerAudience]);
        token.Subject.ShouldBe(AccountId);
        token.GetPayloadValue<string>("token_use").ShouldBe("user");
        token.GetPayloadValue<string>("scope").ShouldBe(UserScopes.Impersonation);
        token.GetPayloadValue<string>("client_id").ShouldBe(ActorId);
        token.GetPayloadValue<long>("auth_time").ShouldBe(_time.GetUtcNow().ToUnixTimeSeconds() - 300);
        token.TryGetPayloadValue<string>("actort", out _).ShouldBeFalse();
        token.TryGetPayloadValue<string>("organization", out _).ShouldBeFalse();
        token.TryGetPayloadValue<string>("name", out _).ShouldBeFalse();

        var payload = Payload(token);
        payload.GetProperty("act").GetRawText().ShouldBe("""{"sub":"mcp"}""");
        payload.GetProperty("restrictions").GetRawText().ShouldBe("""["NoSecretReveal"]""");

        _access.Verify(service => service.GetAccountAsync(AccountId), Times.Once);
        _access.Verify(service => service.CreateAccountAsync(It.IsAny<Accessing.Model.AccountCreate>()), Times.Never);
    }

    [Fact]
    public async Task SubjectToken_IsValidatedForTheActorsAudiencesAndClients()
    {
        _exchangeOptions.Actors[0].SubjectAudiences = [McpResource, "api://mcp-client"];
        _exchangeOptions.Actors[0].AllowedClientIds = [ClientId, "client_other"];

        await _exchange.ExchangeAsync(Request());

        var call = _subjects.Calls.ShouldHaveSingleItem();
        call.SubjectToken.ShouldBe(SubjectToken);
        call.Audiences.ShouldBe([McpResource, "api://mcp-client"]);
        call.AllowedClientIds.ShouldBe([ClientId, "client_other"]);
    }

    [Fact]
    public async Task Disabled_IsUnsupportedGrantType_WithoutAnyCheck()
    {
        _exchangeOptions.Enabled = false;

        var result = await _exchange.ExchangeAsync(Request());

        ShouldBeRefused(result, OAuthTokenExchange.Errors.UnsupportedGrantType, UserTokenExchangeResult.DisabledDescription);
        _subjects.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("no grant_type", OAuthTokenExchange.Errors.InvalidRequest, "The parameter grant_type is missing.")]
    [InlineData("other grant_type", OAuthTokenExchange.Errors.UnsupportedGrantType, "Only the token exchange grant type is supported.")]
    [InlineData("no subject_token", OAuthTokenExchange.Errors.InvalidRequest, "The parameter subject_token is missing.")]
    [InlineData("no subject_token_type", OAuthTokenExchange.Errors.InvalidRequest, "The parameter subject_token_type is missing.")]
    [InlineData("id_token subject", OAuthTokenExchange.Errors.InvalidRequest, "The subject token type is not supported.")]
    [InlineData("jwt requested", OAuthTokenExchange.Errors.InvalidRequest, "The requested token type is not supported.")]
    [InlineData("other audience", OAuthTokenExchange.Errors.InvalidTarget, "The audience or resource is not this API.")]
    [InlineData("other resource", OAuthTokenExchange.Errors.InvalidTarget, "The audience or resource is not this API.")]
    [InlineData("malformed scope", OAuthTokenExchange.Errors.InvalidScope, "The parameter scope is malformed.")]
    [InlineData("no client_id", OAuthTokenExchange.Errors.InvalidRequest, "The parameter client_id is missing.")]
    [InlineData("two proofs", OAuthTokenExchange.Errors.InvalidRequest, "The request must carry one actor proof: actor_token, client_assertion or client_secret.")]
    public async Task MalformedRequest_IsRefused_BeforeTheSubjectToken(string malformation, string error, string description)
    {
        var request = malformation switch
        {
            "no grant_type" => Request() with { GrantType = null },
            "other grant_type" => Request() with { GrantType = "client_credentials" },
            "no subject_token" => Request() with { SubjectToken = null },
            "no subject_token_type" => Request() with { SubjectTokenType = null },
            "id_token subject" => Request() with { SubjectTokenType = OAuthTokenExchange.TokenTypes.IdToken },
            "jwt requested" => Request() with { RequestedTokenType = OAuthTokenExchange.TokenTypes.Jwt },
            "other audience" => Request() with { Audiences = [StorytellerAudience, McpResource] },
            "other resource" => Request() with { Resources = ["https://storyteller.example/api/"] },
            "malformed scope" => Request() with { Scope = "User.Impersonation  Annotation.Read" },
            "no client_id" => Request() with { ClientId = null },
            _ => Request() with { ActorToken = "a.b.c", ActorTokenType = OAuthTokenExchange.TokenTypes.Jwt },
        };

        ShouldBeRefused(await _exchange.ExchangeAsync(request), error, description);
        _subjects.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task TheApiAsAudienceAndResource_AndAnAccessTokenRequested_AreAccepted()
    {
        var result = await _exchange.ExchangeAsync(Request() with
        {
            Audiences = [StorytellerAudience],
            Resources = [StorytellerAudience],
            RequestedTokenType = OAuthTokenExchange.TokenTypes.AccessToken,
            SubjectTokenType = OAuthTokenExchange.TokenTypes.Jwt,
        });

        result.Succeeded.ShouldBeTrue();
    }

    [Theory]
    [InlineData("unknown actor")]
    [InlineData("wrong secret")]
    [InlineData("no proof")]
    public async Task ActorNotAccepted_IsInvalidClient_BeforeTheSubjectToken(string refusal)
    {
        var request = refusal switch
        {
            "unknown actor" => Request() with { ClientId = "another-actor" },
            "wrong secret" => Request() with { ClientSecret = "guessed" },
            _ => Request() with { ClientSecret = null },
        };

        ShouldBeRefused(await _exchange.ExchangeAsync(request), OAuthTokenExchange.Errors.InvalidClient, UserTokenExchangeResult.ActorNotAcceptedDescription);
        _subjects.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task ClientSecret_OutsideDevelopment_IsInvalidClient()
    {
        _environment.EnvironmentName = "Production";

        ShouldBeRefused(await _exchange.ExchangeAsync(Request()), OAuthTokenExchange.Errors.InvalidClient, UserTokenExchangeResult.ActorNotAcceptedDescription);
    }

    [Fact]
    public async Task ClientAssertion_OfTheActor_IsExchanged_Once()
    {
        _exchangeOptions.Actors =
        [
            new TokenExchangeActorOptions
            {
                Id = ActorId,
                Authentication = new ActorAuthenticationOptions { ClientAssertion = new ActorClientAssertionOptions { JwksUri = ActorJwksUri } },
                SubjectAudiences = [McpResource],
            },
        ];
        var now = _time.GetUtcNow().ToUnixTimeSeconds();
        var assertion = _actorKey.Sign(new { iss = ActorId, sub = ActorId, aud = TokenEndpoint, jti = Guid.NewGuid().ToString("D"), iat = now, exp = now + 60 });
        var request = Request() with
        {
            ClientSecret = null,
            ClientAssertion = assertion,
            ClientAssertionType = ActorAuthenticator.ClientAssertionType,
            TokenEndpoint = TokenEndpoint,
        };

        (await _exchange.ExchangeAsync(request)).Succeeded.ShouldBeTrue();
        ShouldBeRefused(await _exchange.ExchangeAsync(request), OAuthTokenExchange.Errors.InvalidClient, UserTokenExchangeResult.ActorNotAcceptedDescription);
        _logger.Entries.ShouldContain(entry => entry.Message.Contains("jti was used before"));
    }

    [Fact]
    public async Task UnreachableActorKeys_AreUnavailable()
    {
        _exchangeOptions.Actors[0].Authentication = new ActorAuthenticationOptions { ClientAssertion = new ActorClientAssertionOptions { JwksUri = ActorJwksUri } };
        _http.SetJson(ActorJwksUri, "{}", System.Net.HttpStatusCode.BadGateway);
        var now = _time.GetUtcNow().ToUnixTimeSeconds();
        var assertion = _actorKey.Sign(new { iss = ActorId, sub = ActorId, aud = StorytellerIssuer, jti = "1", iat = now, exp = now + 60 });

        var result = await _exchange.ExchangeAsync(Request() with { ClientSecret = null, ClientAssertion = assertion, ClientAssertionType = ActorAuthenticator.ClientAssertionType });

        result.IsUnavailable.ShouldBeTrue();
        result.Error.ShouldBe(OAuthTokenExchange.Errors.TemporarilyUnavailable);
        result.ErrorDescription.ShouldBe(UserTokenExchangeResult.KeysUnavailableDescription);
        _subjects.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task RefusedSubjectToken_IsInvalidGrant_WithAFixedDescription_AndTheReasonLogged()
    {
        _subjects.Result = UserSubjectTokenResult.Refused("The token's aud is not one of the actor's SubjectAudiences.");

        var result = await _exchange.ExchangeAsync(Request());

        ShouldBeRefused(result, OAuthTokenExchange.Errors.InvalidGrant, UserTokenExchangeResult.SubjectNotAcceptedDescription);
        _logger.Entries.ShouldContain(entry => entry.Level == Microsoft.Extensions.Logging.LogLevel.Information
            && entry.Message.Contains("SubjectAudiences"));
        _access.Verify(service => service.GetAccountAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task OversizedSubjectToken_IsInvalidGrant_WithoutValidation()
    {
        _options.MaxSubjectTokenBytes = 1024;

        var result = await _exchange.ExchangeAsync(Request() with { SubjectToken = new string('x', 1025) });

        ShouldBeRefused(result, OAuthTokenExchange.Errors.InvalidGrant, UserTokenExchangeResult.SubjectNotAcceptedDescription);
        _subjects.Calls.ShouldBeEmpty();
    }

    // Chained delegation is never issued, whatever the provider let through.
    [Fact]
    public async Task SubjectTokenThatNamesAnActor_IsInvalidGrant()
    {
        _subjects.Result = Subject(claims: [new Claim("sub", AccountId), new Claim("act", """{"sub":"another-actor"}""")]);

        ShouldBeRefused(await _exchange.ExchangeAsync(Request()), OAuthTokenExchange.Errors.InvalidGrant, UserTokenExchangeResult.SubjectNotAcceptedDescription);
        _access.Verify(service => service.GetAccountAsync(It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task SubjectTokenThatExpiresWithinSeconds_IsInvalidGrant(int expiresIn)
    {
        _subjects.Result = Subject(expiresAt: _time.GetUtcNow().AddSeconds(expiresIn));

        ShouldBeRefused(await _exchange.ExchangeAsync(Request()), OAuthTokenExchange.Errors.InvalidGrant, UserTokenExchangeResult.SubjectNotAcceptedDescription);
        _signingKeys.Creates.ShouldBe(0);
    }

    [Fact]
    public async Task UnreachableProviderKeys_AreUnavailable()
    {
        _subjects.Error = new BearerKeyRetrievalException("The realm's keys could not be retrieved.");

        var result = await _exchange.ExchangeAsync(Request());

        result.IsUnavailable.ShouldBeTrue();
        result.ErrorDescription.ShouldBe(UserTokenExchangeResult.KeysUnavailableDescription);
    }

    [Fact]
    public async Task ReplayLimit_AllowsTheConfiguredExchangesPerSubjectTokenAndMinute()
    {
        _exchangeOptions.MaxExchangesPerSubjectTokenPerMinute = 3;

        for (var exchange = 0; exchange < 3; exchange++)
        {
            (await _exchange.ExchangeAsync(Request())).Succeeded.ShouldBeTrue();
        }

        // The generic text of a refused subject token; the reason is only logged.
        ShouldBeRefused(await _exchange.ExchangeAsync(Request()), OAuthTokenExchange.Errors.InvalidGrant, UserTokenExchangeResult.SubjectNotAcceptedDescription);
        _logger.Entries.ShouldContain(entry => entry.Message.Contains("the subject token was exchanged 3 times within a minute"));

        // Another token of the person is counted on its own.
        _subjects.Result = Subject(tokenId: "jti-2");
        (await _exchange.ExchangeAsync(Request())).Succeeded.ShouldBeTrue();

        _time.Advance(TimeSpan.FromMinutes(1));
        _subjects.Result = Subject();
        (await _exchange.ExchangeAsync(Request())).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task ReplayLimit_CountsPerActor()
    {
        _exchangeOptions.MaxExchangesPerSubjectTokenPerMinute = 1;
        _exchangeOptions.Actors = [SecretActor(ActorId), SecretActor("mcp-staging")];

        (await _exchange.ExchangeAsync(Request())).Succeeded.ShouldBeTrue();
        (await _exchange.ExchangeAsync(Request() with { ClientId = "mcp-staging" })).Succeeded.ShouldBeTrue();
        ShouldBeRefused(await _exchange.ExchangeAsync(Request()), OAuthTokenExchange.Errors.InvalidGrant, UserTokenExchangeResult.SubjectNotAcceptedDescription);
    }

    // Without jti (or uti), the token itself is counted.
    [Fact]
    public async Task ReplayLimit_OfATokenWithoutAnId_CountsTheToken()
    {
        _exchangeOptions.MaxExchangesPerSubjectTokenPerMinute = 1;
        _subjects.Result = Subject(tokenId: null);

        (await _exchange.ExchangeAsync(Request())).Succeeded.ShouldBeTrue();
        ShouldBeRefused(await _exchange.ExchangeAsync(Request()), OAuthTokenExchange.Errors.InvalidGrant, UserTokenExchangeResult.SubjectNotAcceptedDescription);
        (await _exchange.ExchangeAsync(Request() with { SubjectToken = SubjectToken + "x" })).Succeeded.ShouldBeTrue();
    }

    // The account is looked up before the count, so retries without an account keep the registration hint.
    [Fact]
    public async Task ReplayLimit_DoesNotCountASignInWithoutAnAccount()
    {
        _exchangeOptions.MaxExchangesPerSubjectTokenPerMinute = 1;
        _subjects.Result = Subject(accountId: "user_unregistered");

        for (var exchange = 0; exchange < 3; exchange++)
        {
            ShouldBeRefused(await _exchange.ExchangeAsync(Request()), OAuthTokenExchange.Errors.InvalidGrant, UserTokenExchangeResult.NoAccountDescription);
        }

        _access.Verify(service => service.GetAccountAsync("user_unregistered"), Times.Exactly(3));
    }

    [Fact]
    public async Task SignInWithoutAnAccount_IsInvalidGrant_WithTheRegistrationHint()
    {
        _subjects.Result = Subject(accountId: "user_unregistered");

        var result = await _exchange.ExchangeAsync(Request());

        ShouldBeRefused(result, OAuthTokenExchange.Errors.InvalidGrant, "No Storyteller account exists for this sign-in. Register once with sform or the admin UI.");
        _access.Verify(service => service.GetAccountAsync("user_unregistered"), Times.Once);
        _access.Verify(service => service.CreateAccountAsync(It.IsAny<Accessing.Model.AccountCreate>()), Times.Never);
        _signingKeys.Creates.ShouldBe(0);
    }

    [Theory]
    [InlineData(null, "User.Impersonation Annotation.Read")]
    [InlineData("User.Impersonation", "User.Impersonation")]
    [InlineData("Annotation.Read User.Impersonation", "Annotation.Read User.Impersonation")]
    [InlineData("User.Impersonation Configuration.Read", "User.Impersonation")]
    [InlineData("User.Impersonation User.Impersonation", "User.Impersonation")]
    public async Task Scope_IsTheRequestedOneWithinTheActors(string? requested, string granted)
    {
        _exchangeOptions.Actors[0].Scopes = [UserScopes.Impersonation, "Annotation.Read"];

        var result = await _exchange.ExchangeAsync(Request() with { Scope = requested });

        result.Succeeded.ShouldBeTrue();
        result.Scope.ShouldBe(granted);
        new JsonWebToken(result.AccessToken).GetPayloadValue<string>("scope").ShouldBe(granted);
    }

    [Theory]
    [InlineData("Annotation.Read")]
    [InlineData("Configuration.Read")]
    [InlineData("user.impersonation")]
    public async Task Scope_WithoutUserImpersonation_IsInvalidScope(string requested)
    {
        _exchangeOptions.Actors[0].Scopes = [UserScopes.Impersonation, "Annotation.Read"];

        ShouldBeRefused(await _exchange.ExchangeAsync(Request() with { Scope = requested }), OAuthTokenExchange.Errors.InvalidScope, UserTokenExchangeResult.ScopeNotGrantedDescription);
        _signingKeys.Creates.ShouldBe(0);
    }

    [Theory]
    [InlineData(3600, 10, 600)]
    [InlineData(3600, 5, 300)]
    [InlineData(180, 10, 180)]
    public async Task Lifetime_IsTheSettingButNeverPastTheSubjectToken(int subjectExpiresIn, int lifetimeMinutes, int expiresIn)
    {
        _exchangeOptions.TokenLifetimeMinutes = lifetimeMinutes;
        _subjects.Result = Subject(expiresAt: _time.GetUtcNow().AddSeconds(subjectExpiresIn));

        var result = await _exchange.ExchangeAsync(Request());

        result.ExpiresIn.ShouldBe(expiresIn);
        var token = new JsonWebToken(result.AccessToken);
        (token.ValidTo - token.IssuedAt).ShouldBe(TimeSpan.FromSeconds(expiresIn));
        token.ValidTo.ShouldBeLessThanOrEqualTo(_subjects.Result.ExpiresAt.UtcDateTime);
    }

    [Theory]
    [InlineData(false, new[] { "NoSecretReveal" })]
    [InlineData(true, new string[0])]
    public async Task Restrictions_AreNoSecretRevealUnlessTheActorAllowsSecrets(bool allowSecrets, string[] restrictions)
    {
        _exchangeOptions.Actors[0].AllowSecrets = allowSecrets;

        var result = await _exchange.ExchangeAsync(Request());

        result.Restrictions.ShouldBe(restrictions);
        Payload(new JsonWebToken(result.AccessToken)).GetProperty("restrictions").EnumerateArray().Select(value => value.GetString()).ShouldBe(restrictions);
    }

    [Fact]
    public async Task Restrictions_NamedBesidesTheDefault_AreCarriedOnce()
    {
        _exchangeOptions.Actors[0].Restrictions = [ActorRestrictions.NoSecretReveal];

        (await _exchange.ExchangeAsync(Request())).Restrictions.ShouldBe([ActorRestrictions.NoSecretReveal]);
    }

    [Fact]
    public async Task NoSigningKey_IsUnavailable()
    {
        _options.SigningKeyAutoBootstrap = false;

        var result = await _exchange.ExchangeAsync(Request());

        result.IsUnavailable.ShouldBeTrue();
        result.ErrorDescription.ShouldBe(UserTokenExchangeResult.SigningKeyUnavailableDescription);
    }

    [Fact]
    public async Task Logs_NeverCarryATokenOrTheSecret()
    {
        var issued = await _exchange.ExchangeAsync(Request());
        _subjects.Result = UserSubjectTokenResult.Refused("The token is not valid (SecurityTokenExpiredException).");
        await _exchange.ExchangeAsync(Request());
        await _exchange.ExchangeAsync(Request() with { ClientSecret = "guessed" });

        _logger.Entries.Count.ShouldBeGreaterThanOrEqualTo(3);
        _logger.Entries.ShouldAllBe(entry => !entry.Message.Contains(SubjectToken)
            && !entry.Message.Contains(Secret)
            && !entry.Message.Contains("guessed")
            && !entry.Message.Contains(issued.AccessToken!));
    }

    [Fact]
    public void Request_ToString_LeavesOutTokensAndTheSecret()
    {
        var text = (Request() with { ActorToken = "actor.token.value", ClientAssertion = "client.assertion.value" }).ToString();

        text.ShouldContain(ActorId);
        text.ShouldNotContain(SubjectToken);
        text.ShouldNotContain(Secret);
        text.ShouldNotContain("actor.token.value");
        text.ShouldNotContain("client.assertion.value");
    }

    private static TokenExchangeActorOptions SecretActor(string id)
    {
        return new TokenExchangeActorOptions
        {
            Id = id,
            Authentication = new ActorAuthenticationOptions { ClientSecretHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Secret))) },
            SubjectAudiences = [McpResource],
            AllowedClientIds = [ClientId],
        };
    }

    private static UserTokenExchangeRequest Request()
    {
        return new UserTokenExchangeRequest
        {
            GrantType = OAuthTokenExchange.GrantType,
            ClientId = ActorId,
            ClientSecret = Secret,
            SubjectToken = SubjectToken,
            SubjectTokenType = OAuthTokenExchange.TokenTypes.AccessToken,
        };
    }

    private static void ShouldBeRefused(UserTokenExchangeResult result, string error, string description)
    {
        result.Succeeded.ShouldBeFalse();
        result.IsUnavailable.ShouldBeFalse();
        result.AccessToken.ShouldBeNull();
        result.Error.ShouldBe(error);
        result.ErrorDescription.ShouldBe(description);
    }

    private static JsonElement Payload(JsonWebToken token)
    {
        using var document = JsonDocument.Parse(Base64UrlEncoder.Decode(token.EncodedPayload));
        return document.RootElement.Clone();
    }

    private UserSubjectTokenResult Subject(
        string accountId = AccountId,
        DateTimeOffset? expiresAt = null,
        string? tokenId = "jti-1",
        IReadOnlyList<Claim>? claims = null)
    {
        var now = _time.GetUtcNow();
        return UserSubjectTokenResult.Accepted(
            accountId,
            ClientId,
            expiresAt ?? now.AddHours(1),
            DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds() - 300),
            tokenId,
            claims ?? [new Claim("sub", accountId)]);
    }
}
