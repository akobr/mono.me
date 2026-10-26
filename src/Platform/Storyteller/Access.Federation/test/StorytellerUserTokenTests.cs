using System.Text.Json;
using _42.Platform.Storyteller.Accessing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Access.Federation.UnitTests;

public sealed class StorytellerUserTokenTests
{
    private const string Issuer = "urn:storyteller:2s.test";
    private const string Audience = "urn:storyteller:2s.test:api";
    private const string AccountId = "user_01HZX3Y4QJ7K9M2N";

    private readonly ManualTimeProvider _time = new(DateTimeOffset.FromUnixTimeSeconds(1_793_000_000));
    private readonly CountingSigningKeyStore _store = new();
    private readonly FederationOptions _options = new() { Issuer = Issuer, Audience = Audience };
    private readonly StorytellerTokenIssuer _issuer;

    public StorytellerUserTokenTests()
    {
        var options = Options.Create(_options);
        _issuer = new StorytellerTokenIssuer(new TokenSigningKeyProvider(_store, options, _time, NullLogger<TokenSigningKeyProvider>.Instance), options, _time);
    }

    [Fact]
    public async Task UserToken_IsAnRfc9068AccessTokenOfThePersonThatNamesTheActor()
    {
        var issued = await _issuer.IssueUserAsync(Request());

        issued.ExpiresIn.ShouldBe(600);
        issued.ExpiresAt.ShouldBe(_time.Now.AddMinutes(10));

        var token = new JsonWebToken(issued.AccessToken);
        token.Typ.ShouldBe("at+jwt");
        token.Alg.ShouldBe("RS256");
        token.Kid.ShouldBe(_store.Key!.Kid);
        token.Issuer.ShouldBe(Issuer);
        token.Audiences.ShouldBe([Audience]);
        token.Subject.ShouldBe(AccountId);
        token.GetPayloadValue<string>("token_use").ShouldBe("user");
        token.GetPayloadValue<string>("scope").ShouldBe("User.Impersonation Annotation.Read");
        token.GetPayloadValue<string>("client_id").ShouldBe("mcp");
        token.GetPayloadValue<long>("auth_time").ShouldBe(1_792_999_000);
        Guid.TryParse(token.Id, out _).ShouldBeTrue();
        token.IssuedAt.ShouldBe(_time.Now.UtcDateTime);
        token.ValidFrom.ShouldBe(token.IssuedAt);
        (token.ValidTo - token.IssuedAt).ShouldBe(TimeSpan.FromMinutes(10));

        var payload = Payload(token);
        payload.GetProperty("act").ValueKind.ShouldBe(JsonValueKind.Object);
        payload.GetProperty("act").EnumerateObject().Select(member => (member.Name, member.Value.GetString())).ShouldBe([("sub", "mcp")]);
        payload.GetProperty("restrictions").ValueKind.ShouldBe(JsonValueKind.Array);
        payload.GetProperty("restrictions").EnumerateArray().Select(value => value.GetString()).ShouldBe(["NoSecretReveal"]);
    }

    // Delegation (RFC 8693 act), not the actort of SecurityTokenDescriptor.Subject.Actor; nothing of a machine or a name.
    [Fact]
    public async Task UserToken_CarriesNoActortMachineClaimsOrName()
    {
        var payload = Payload(new JsonWebToken((await _issuer.IssueUserAsync(Request())).AccessToken));

        payload.EnumerateObject().Select(member => member.Name).ShouldBe(
            ["sub", "token_use", "scope", "act", "client_id", "restrictions", "jti", "auth_time", "aud", "exp", "iat", "iss", "nbf"],
            ignoreOrder: true);
    }

    [Fact]
    public async Task UserToken_WithoutRestrictions_HasAnEmptyArray_AndWithoutAuthTime_NoSuchClaim()
    {
        var issued = await _issuer.IssueUserAsync(Request() with { Restrictions = [], AuthTime = null });

        var payload = Payload(new JsonWebToken(issued.AccessToken));
        payload.GetProperty("restrictions").GetArrayLength().ShouldBe(0);
        payload.TryGetProperty("auth_time", out _).ShouldBeFalse();
    }

    // The token never outlives the subject token, and lives whole seconds.
    [Theory]
    [InlineData(3600, 600)]
    [InlineData(600, 600)]
    [InlineData(180.9, 180)]
    [InlineData(1, 1)]
    public async Task UserToken_LivesTheLifetime_ButNeverPastTheSubjectToken(double subjectExpiresIn, int expiresIn)
    {
        var issued = await _issuer.IssueUserAsync(Request() with { NotAfter = _time.Now.AddSeconds(subjectExpiresIn) });

        issued.ExpiresIn.ShouldBe(expiresIn);
        var token = new JsonWebToken(issued.AccessToken);
        (token.ValidTo - token.IssuedAt).ShouldBe(TimeSpan.FromSeconds(expiresIn));
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(0)]
    [InlineData(-30)]
    public async Task UserToken_ForASubjectTokenThatExpires_IsNotIssued(double subjectExpiresIn)
    {
        await Should.ThrowAsync<ArgumentException>(() => _issuer.IssueUserAsync(Request() with { NotAfter = _time.Now.AddSeconds(subjectExpiresIn) }));
        _store.Creates.ShouldBe(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    public async Task UserToken_OutsideTheLifetimeRange_IsNotIssued(int minutes)
    {
        await Should.ThrowAsync<ArgumentOutOfRangeException>(() => _issuer.IssueUserAsync(Request() with { MaxLifetime = TimeSpan.FromMinutes(minutes) }));
    }

    [Fact]
    public async Task UserToken_IsSignedByTheKeyOfTheJwks()
    {
        var keyProvider = new TokenSigningKeyProvider(_store, Options.Create(_options), _time, NullLogger<TokenSigningKeyProvider>.Instance);
        var issued = await new StorytellerTokenIssuer(keyProvider, Options.Create(_options), _time).IssueUserAsync(Request());

        var jwk = (await keyProvider.GetPublicJsonWebKeysAsync()).ShouldHaveSingleItem();
        var key = new JsonWebKey { Kty = jwk.Kty, Kid = jwk.Kid, N = jwk.N, E = jwk.E, Alg = jwk.Alg, Use = jwk.Use };
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(issued.AccessToken, new TokenValidationParameters
        {
            IssuerSigningKey = key,
            ValidIssuer = Issuer,
            ValidAudience = Audience,
            ValidAlgorithms = ["RS256"],
            ValidateLifetime = false,
        });

        result.IsValid.ShouldBeTrue();
    }

    private static JsonElement Payload(JsonWebToken token)
    {
        using var document = JsonDocument.Parse(Base64UrlEncoder.Decode(token.EncodedPayload));
        return document.RootElement.Clone();
    }

    private StorytellerUserTokenRequest Request()
    {
        return new StorytellerUserTokenRequest
        {
            AccountId = AccountId,
            ActorId = "mcp",
            Scopes = [UserScopes.Impersonation, "Annotation.Read"],
            Restrictions = [ActorRestrictions.NoSecretReveal],
            AuthTime = DateTimeOffset.FromUnixTimeSeconds(1_792_999_000),
            MaxLifetime = TimeSpan.FromMinutes(10),
            NotAfter = _time.Now.AddHours(1),
        };
    }
}
