using System.Net;
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

namespace _42.Platform.Storyteller.Access.Federation.UnitTests;

public sealed class ActorAuthenticatorTests : IDisposable
{
    private const string ActorId = "mcp";
    private const string StorytellerIssuer = "https://storyteller.example";
    private const string TokenEndpoint = "https://storyteller.example/api/v1/access/token";
    private const string WorkloadIssuer = "https://login.example/tenant-1/v2.0";
    private const string WorkloadJwksUri = "https://login.example/tenant-1/discovery/v2.0/keys";
    private const string WorkloadSubject = "0b6f5a9e-3d2c-4c1e-9f7a-2e8d4b6c1a35";
    private const string WorkloadAudience = "api://storyteller-token-exchange";
    private const string ActorJwksUri = "https://mcp.example/.well-known/jwks.json";
    private const string Secret = "a local development secret";

    private readonly ManualTimeProvider _time = new();
    private readonly StubHttpMessageHandler _http = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly TestSigningKey _workloadKey = TestSigningKey.Rsa("workload-1");
    private readonly TestSigningKey _actorKey = TestSigningKey.EllipticCurve("mcp-1");
    private readonly FederationOptions _options = new() { Issuer = StorytellerIssuer, Audience = "https://storyteller.example/api" };
    private readonly TokenExchangeOptions _exchangeOptions = new() { Enabled = true };
    private readonly TestHostEnvironment _environment = new();
    private readonly ActorAuthenticator _authenticator;

    public ActorAuthenticatorTests()
    {
        var options = Options.Create(_options);
        var resolver = new FederatedIssuerKeyResolver(new StubHttpClientFactory(_http), _cache, options, _time, NullLogger<FederatedIssuerKeyResolver>.Instance);
        _authenticator = new ActorAuthenticator(resolver, Options.Create(_exchangeOptions), options, _environment, _time);

        _http.SetJson(WorkloadJwksUri, TestSigningKey.Jwks(_workloadKey));
        _http.SetJson(ActorJwksUri, TestSigningKey.Jwks(_actorKey));
    }

    public void Dispose()
    {
        _authenticator.Dispose();
        _workloadKey.Dispose();
        _actorKey.Dispose();
        _http.Dispose();
        _cache.Dispose();
    }

    [Theory]
    [InlineData(OAuthTokenExchange.TokenTypes.Jwt)]
    [InlineData(OAuthTokenExchange.TokenTypes.AccessToken)]
    public async Task WorkloadIdentityToken_OfTheActor_IsAccepted(string tokenType)
    {
        UseWorkloadIdentity();

        var result = await _authenticator.AuthenticateAsync(WithActorToken(_workloadKey.Sign(WorkloadPayload()), tokenType));

        result.Succeeded.ShouldBeTrue();
        result.Actor.Id.ShouldBe(ActorId);
    }

    // Managed identity tokens live about a day: the hour of machine platform tokens does not apply.
    [Fact]
    public async Task WorkloadIdentityToken_LivingADay_IsAccepted()
    {
        UseWorkloadIdentity();
        var payload = WorkloadPayload();
        payload["exp"] = Now() + 86400;

        (await _authenticator.AuthenticateAsync(WithActorToken(_workloadKey.Sign(payload)))).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task WorkloadIdentityToken_WithTheAudienceAmongOthers_IsAccepted()
    {
        UseWorkloadIdentity();
        var payload = WorkloadPayload();
        payload["aud"] = new[] { "https://management.azure.com", WorkloadAudience };

        (await _authenticator.AuthenticateAsync(WithActorToken(_workloadKey.Sign(payload)))).Succeeded.ShouldBeTrue();
    }

    [Fact]
    public async Task WorkloadIdentityToken_WithKeysFromDiscovery_IsAccepted()
    {
        UseWorkloadIdentity(jwksUri: null);
        _http.SetJson($"{WorkloadIssuer}/.well-known/openid-configuration", JsonSerializer.Serialize(new { issuer = WorkloadIssuer, jwks_uri = WorkloadJwksUri }));

        (await _authenticator.AuthenticateAsync(WithActorToken(_workloadKey.Sign(WorkloadPayload())))).Succeeded.ShouldBeTrue();
        _http.Count($"{WorkloadIssuer}/.well-known/openid-configuration").ShouldBe(1);
    }

    [Theory]
    [InlineData("sub", "another-identity")]
    [InlineData("aud", "api://another-audience")]
    [InlineData("exp", "expired")]
    [InlineData("nbf", "future")]
    public async Task WorkloadIdentityToken_NotOfTheActor_IsInvalidClient(string claim, string value)
    {
        UseWorkloadIdentity();
        var payload = WorkloadPayload();
        payload[claim] = value switch
        {
            "expired" => Now() - 120,
            "future" => Now() + 120,
            _ => value,
        };

        ShouldBeNotAccepted(await _authenticator.AuthenticateAsync(WithActorToken(_workloadKey.Sign(payload))));
    }

    [Fact]
    public async Task WorkloadIdentityToken_OfAnotherIssuer_IsInvalidClient_WithoutFetchingKeys()
    {
        UseWorkloadIdentity();
        var payload = WorkloadPayload();
        payload["iss"] = "https://login.example/tenant-2/v2.0";

        ShouldBeNotAccepted(await _authenticator.AuthenticateAsync(WithActorToken(_workloadKey.Sign(payload))));
        _http.TotalRequests.ShouldBe(0);
    }

    [Fact]
    public async Task WorkloadIdentityToken_SignedWithAnotherKey_IsInvalidClient()
    {
        UseWorkloadIdentity();
        using var other = TestSigningKey.Rsa("workload-1");

        ShouldBeNotAccepted(await _authenticator.AuthenticateAsync(WithActorToken(other.Sign(WorkloadPayload()))));
    }

    [Fact]
    public async Task WorkloadIdentityKeys_Unreachable_AreUnavailable()
    {
        UseWorkloadIdentity();
        _http.SetJson(WorkloadJwksUri, "{}", HttpStatusCode.ServiceUnavailable);

        await Should.ThrowAsync<BearerKeyRetrievalException>(() => _authenticator.AuthenticateAsync(WithActorToken(_workloadKey.Sign(WorkloadPayload()))));
    }

    [Fact]
    public async Task ActorToken_OfAnUnsupportedType_IsInvalidRequest()
    {
        UseWorkloadIdentity();

        var result = await _authenticator.AuthenticateAsync(WithActorToken(_workloadKey.Sign(WorkloadPayload()), OAuthTokenExchange.TokenTypes.IdToken));

        result.Error.ShouldBe(OAuthTokenExchange.Errors.InvalidRequest);
        result.ErrorDescription.ShouldBe("The actor token type is not supported.");
    }

    [Theory]
    [InlineData(TokenEndpoint, TokenEndpoint)]
    [InlineData(StorytellerIssuer, TokenEndpoint)]
    [InlineData(StorytellerIssuer, null)]
    public async Task ClientAssertion_ForThisTokenEndpointOrTheIssuer_IsAccepted(string audience, string? tokenEndpoint)
    {
        UseClientAssertion();

        var result = await _authenticator.AuthenticateAsync(WithAssertion(_actorKey.Sign(AssertionPayload(audience)), tokenEndpoint));

        result.Succeeded.ShouldBeTrue();
        result.Actor.Id.ShouldBe(ActorId);
    }

    [Theory]
    [InlineData("https://other.example/oauth2/token", TokenEndpoint)]
    [InlineData(TokenEndpoint, null)]
    [InlineData("https://storyteller.example/", TokenEndpoint)]
    public async Task ClientAssertion_ForAnotherAudience_IsInvalidClient(string audience, string? tokenEndpoint)
    {
        UseClientAssertion();

        ShouldBeNotAccepted(await _authenticator.AuthenticateAsync(WithAssertion(_actorKey.Sign(AssertionPayload(audience)), tokenEndpoint)));
    }

    [Fact]
    public async Task ClientAssertion_WhoseSubIsNotItsIss_IsInvalidClient()
    {
        UseClientAssertion();
        var payload = AssertionPayload();
        payload["sub"] = "someone-else";

        ShouldBeNotAccepted(await _authenticator.AuthenticateAsync(WithAssertion(_actorKey.Sign(payload))));
    }

    [Fact]
    public async Task ClientAssertion_OfAnotherIssuer_IsInvalidClient_WithoutFetchingKeys()
    {
        UseClientAssertion();
        var payload = AssertionPayload();
        payload["iss"] = "someone-else";
        payload["sub"] = "someone-else";

        ShouldBeNotAccepted(await _authenticator.AuthenticateAsync(WithAssertion(_actorKey.Sign(payload))));
        _http.TotalRequests.ShouldBe(0);
    }

    [Theory]
    [InlineData(55, true)]
    [InlineData(60, true)]
    [InlineData(61, false)]
    [InlineData(300, false)]
    [InlineData(3600, false)]
    public async Task ClientAssertion_LivesAtMostOneMinute(int lifetime, bool accepted)
    {
        UseClientAssertion();
        var payload = AssertionPayload();
        payload["iat"] = Now() - 10;
        payload["exp"] = Now() - 10 + lifetime;

        (await _authenticator.AuthenticateAsync(WithAssertion(_actorKey.Sign(payload)))).Succeeded.ShouldBe(accepted);
    }

    // Without iat, the time left counts, with the clock skew.
    [Theory]
    [InlineData(60, true)]
    [InlineData(90, true)]
    [InlineData(91, false)]
    [InlineData(300, false)]
    public async Task ClientAssertion_WithoutIat_ExpiresWithinOneMinute(int expiresIn, bool accepted)
    {
        UseClientAssertion();
        var payload = AssertionPayload();
        payload.Remove("iat");
        payload["exp"] = Now() + expiresIn;

        (await _authenticator.AuthenticateAsync(WithAssertion(_actorKey.Sign(payload)))).Succeeded.ShouldBe(accepted);
    }

    [Theory]
    [InlineData("exp")]
    [InlineData("jti")]
    public async Task ClientAssertion_Without_IsInvalidClient(string claim)
    {
        UseClientAssertion();
        var payload = AssertionPayload();
        payload.Remove(claim);

        ShouldBeNotAccepted(await _authenticator.AuthenticateAsync(WithAssertion(_actorKey.Sign(payload))));
    }

    [Fact]
    public async Task ClientAssertion_Expired_IsInvalidClient()
    {
        UseClientAssertion();
        var payload = AssertionPayload();
        payload["iat"] = Now() - 100;
        payload["exp"] = Now() - 60;

        ShouldBeNotAccepted(await _authenticator.AuthenticateAsync(WithAssertion(_actorKey.Sign(payload))));
    }

    [Fact]
    public async Task ClientAssertion_WithAnIatThatIsNoNumber_IsInvalidClient()
    {
        UseClientAssertion();
        var payload = AssertionPayload();
        payload["iat"] = "now";

        ShouldBeNotAccepted(await _authenticator.AuthenticateAsync(WithAssertion(_actorKey.Sign(payload))));
    }

    [Fact]
    public async Task ClientAssertion_IsUsedOnce()
    {
        UseClientAssertion();
        var assertion = _actorKey.Sign(AssertionPayload());

        (await _authenticator.AuthenticateAsync(WithAssertion(assertion))).Succeeded.ShouldBeTrue();
        ShouldBeNotAccepted(await _authenticator.AuthenticateAsync(WithAssertion(assertion)));

        // Another assertion of the same jti is the same replay.
        var sameId = AssertionPayload();
        sameId["jti"] = new JsonWebToken(assertion).Id;
        ShouldBeNotAccepted(await _authenticator.AuthenticateAsync(WithAssertion(_actorKey.Sign(sameId))));

        (await _authenticator.AuthenticateAsync(WithAssertion(_actorKey.Sign(AssertionPayload())))).Succeeded.ShouldBeTrue();
    }

    // The keys are those of ClientAssertion:JwksUri only: jku and jwk headers of the token are never followed.
    [Fact]
    public async Task ClientAssertion_SignedWithAKeyTheTokenNames_IsInvalidClient_WithoutFetchingIt()
    {
        UseClientAssertion();
        const string attackerJwks = "https://attacker.example/jwks.json";
        using var attacker = TestSigningKey.EllipticCurve(_actorKey.Kid);
        _http.SetJson(attackerJwks, TestSigningKey.Jwks(attacker));
        using var attackerSet = JsonDocument.Parse(TestSigningKey.Jwks(attacker));
        var assertion = new JsonWebTokenHandler().CreateToken(
            JsonSerializer.Serialize(AssertionPayload()),
            new SigningCredentials(attacker.SecurityKey, SecurityAlgorithms.EcdsaSha256),
            new Dictionary<string, object> { ["jku"] = attackerJwks, ["jwk"] = attackerSet.RootElement.GetProperty("keys")[0].Clone() });

        new JsonWebToken(assertion).TryGetHeaderValue<string>("jku", out var jku).ShouldBeTrue();
        jku.ShouldBe(attackerJwks);

        ShouldBeNotAccepted(await _authenticator.AuthenticateAsync(WithAssertion(assertion)));
        _http.Count(attackerJwks).ShouldBe(0);
    }

    [Fact]
    public async Task ClientAssertion_OfAnUnsupportedType_IsInvalidRequest()
    {
        UseClientAssertion();

        var result = await _authenticator.AuthenticateAsync(WithAssertion(_actorKey.Sign(AssertionPayload())) with { ClientAssertionType = "urn:ietf:params:oauth:client-assertion-type:saml2-bearer" });

        result.Error.ShouldBe(OAuthTokenExchange.Errors.InvalidRequest);
        result.ErrorDescription.ShouldBe("The client assertion type is not supported.");
    }

    [Fact]
    public async Task ClientSecret_InDevelopment_IsAccepted()
    {
        UseClientSecret();
        _environment.EnvironmentName = "Development";

        (await _authenticator.AuthenticateAsync(WithSecret(Secret))).Succeeded.ShouldBeTrue();
    }

    // The settings check at start refuses it outside Development; the proof is refused at runtime as well.
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task ClientSecret_OutsideDevelopment_IsInvalidClient(string environment)
    {
        UseClientSecret();
        _environment.EnvironmentName = environment;

        ShouldBeNotAccepted(await _authenticator.AuthenticateAsync(WithSecret(Secret)));
    }

    [Theory]
    [InlineData("another secret")]
    [InlineData("a local development secret ")]
    [InlineData("A local development secret")]
    public async Task WrongClientSecret_IsInvalidClient(string secret)
    {
        UseClientSecret();
        _environment.EnvironmentName = "Development";

        ShouldBeNotAccepted(await _authenticator.AuthenticateAsync(WithSecret(secret)));
    }

    [Fact]
    public async Task UnknownActor_IsInvalidClient_WithoutFetchingKeys()
    {
        UseClientAssertion();

        ShouldBeNotAccepted(await _authenticator.AuthenticateAsync(WithAssertion(_actorKey.Sign(AssertionPayload())) with { ClientId = "MCP" }));
        _http.TotalRequests.ShouldBe(0);
    }

    [Fact]
    public async Task RequestWithoutAProof_IsInvalidClient()
    {
        UseClientAssertion();

        ShouldBeNotAccepted(await _authenticator.AuthenticateAsync(new UserTokenExchangeRequest { ClientId = ActorId }));
    }

    // A valid proof of another method than the actor's is refused like a wrong one.
    [Fact]
    public async Task ProofOfAnotherMethod_IsInvalidClient()
    {
        UseWorkloadIdentity();
        _environment.EnvironmentName = "Development";

        ShouldBeNotAccepted(await _authenticator.AuthenticateAsync(WithAssertion(_actorKey.Sign(AssertionPayload()))));
        ShouldBeNotAccepted(await _authenticator.AuthenticateAsync(WithSecret(Secret)));
    }

    [Theory]
    [InlineData("client_id", "The parameter client_id is missing.")]
    [InlineData("two proofs", "The request must carry one actor proof: actor_token, client_assertion or client_secret.")]
    [InlineData("actor_token_type", "The parameter actor_token_type is missing.")]
    [InlineData("actor_token", "The parameter actor_token is missing.")]
    [InlineData("client_assertion_type", "The parameter client_assertion_type is missing.")]
    [InlineData("client_assertion", "The parameter client_assertion is missing.")]
    public async Task MalformedProof_IsInvalidRequest(string malformation, string description)
    {
        UseWorkloadIdentity();
        var request = WithActorToken(_workloadKey.Sign(WorkloadPayload()));
        request = malformation switch
        {
            "client_id" => request with { ClientId = null },
            "two proofs" => request with { ClientSecret = Secret },
            "actor_token_type" => request with { ActorTokenType = null },
            "actor_token" => request with { ActorToken = null },
            "client_assertion_type" => request with { ClientAssertion = "a.b.c" },
            _ => request with { ClientAssertionType = ActorAuthenticator.ClientAssertionType },
        };

        var result = await _authenticator.AuthenticateAsync(request);

        result.Error.ShouldBe(OAuthTokenExchange.Errors.InvalidRequest);
        result.ErrorDescription.ShouldBe(description);
        _http.TotalRequests.ShouldBe(0);
    }

    private static void ShouldBeNotAccepted(ActorAuthenticationResult result)
    {
        result.Succeeded.ShouldBeFalse();
        result.Error.ShouldBe(OAuthTokenExchange.Errors.InvalidClient);
        result.ErrorDescription.ShouldBe(UserTokenExchangeResult.ActorNotAcceptedDescription);
        result.RefusalReason.ShouldNotBeNullOrWhiteSpace();
    }

    private static UserTokenExchangeRequest WithActorToken(string token, string tokenType = OAuthTokenExchange.TokenTypes.Jwt)
    {
        return new UserTokenExchangeRequest { ClientId = ActorId, ActorToken = token, ActorTokenType = tokenType };
    }

    private static UserTokenExchangeRequest WithAssertion(string assertion, string? tokenEndpoint = TokenEndpoint)
    {
        return new UserTokenExchangeRequest
        {
            ClientId = ActorId,
            ClientAssertion = assertion,
            ClientAssertionType = ActorAuthenticator.ClientAssertionType,
            TokenEndpoint = tokenEndpoint,
        };
    }

    private static UserTokenExchangeRequest WithSecret(string secret)
    {
        return new UserTokenExchangeRequest { ClientId = ActorId, ClientSecret = secret };
    }

    private long Now() => _time.GetUtcNow().ToUnixTimeSeconds();

    private void UseWorkloadIdentity(string? jwksUri = WorkloadJwksUri)
    {
        UseActor(new ActorAuthenticationOptions
        {
            WorkloadIdentity = new ActorWorkloadIdentityOptions
            {
                Issuer = WorkloadIssuer,
                Subject = WorkloadSubject,
                Audience = WorkloadAudience,
                JwksUri = jwksUri,
            },
        });
    }

    private void UseClientAssertion()
    {
        UseActor(new ActorAuthenticationOptions { ClientAssertion = new ActorClientAssertionOptions { JwksUri = ActorJwksUri } });
    }

    private void UseClientSecret()
    {
        UseActor(new ActorAuthenticationOptions { ClientSecretHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Secret))) });
    }

    private void UseActor(ActorAuthenticationOptions authentication)
    {
        _exchangeOptions.Actors =
        [
            new TokenExchangeActorOptions { Id = ActorId, Authentication = authentication, SubjectAudiences = ["https://mcp.example/mcp"] },
        ];
    }

    private Dictionary<string, object> WorkloadPayload()
    {
        var now = Now();
        return new Dictionary<string, object>
        {
            ["iss"] = WorkloadIssuer,
            ["sub"] = WorkloadSubject,
            ["aud"] = WorkloadAudience,
            ["iat"] = now,
            ["nbf"] = now,
            ["exp"] = now + 3600,
        };
    }

    private Dictionary<string, object> AssertionPayload(string audience = TokenEndpoint)
    {
        var now = Now();
        return new Dictionary<string, object>
        {
            ["iss"] = ActorId,
            ["sub"] = ActorId,
            ["aud"] = audience,
            ["jti"] = Guid.NewGuid().ToString("D"),
            ["iat"] = now,
            ["exp"] = now + 55,
        };
    }
}
