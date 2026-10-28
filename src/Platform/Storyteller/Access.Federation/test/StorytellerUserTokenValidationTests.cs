using System.Text;
using System.Text.Json.Nodes;
using _42.Platform.Storyteller.Accessing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Access.Federation.UnitTests;

// Storyteller user tokens on API calls: token_use "user" decides the shape before any machine claim is read, the actor
// must be configured while the exchange is enabled, every restriction must be known, and the actor's settings of now can
// add restrictions but never remove one. Machine tokens are unchanged, except that one naming an actor is refused.
public sealed class StorytellerUserTokenValidationTests
{
    private const string Issuer = "urn:storyteller:2s.test";
    private const string Audience = "urn:storyteller:2s.test:api";
    private const string AccountId = "user_01HZX3Y4QJ7K9M2N";
    private const string ActorId = "mcp";
    private const string MachineId = "6f1c2a8e-4c1b-4d4b-9a51-0f6b0d9a3c11";

    private readonly ManualTimeProvider _time = new(DateTimeOffset.FromUnixTimeSeconds(1_793_000_000));
    private readonly CountingSigningKeyStore _store = new();
    private readonly FederationOptions _options = new() { Issuer = Issuer, Audience = Audience };
    private readonly TokenExchangeOptions _exchangeOptions = new() { Enabled = true, Actors = [new TokenExchangeActorOptions { Id = ActorId, Scopes = [UserScopes.Impersonation, "Annotation.Read"] }] };
    private readonly RecordingLogger<StorytellerTokenValidator> _logger = new();
    private readonly TokenSigningKeyProvider _keyProvider;
    private readonly StorytellerTokenIssuer _issuer;
    private readonly StorytellerTokenValidator _validator;

    public StorytellerUserTokenValidationTests()
    {
        var options = Options.Create(_options);
        _keyProvider = new TokenSigningKeyProvider(_store, options, _time, NullLogger<TokenSigningKeyProvider>.Instance);
        _issuer = new StorytellerTokenIssuer(_keyProvider, options, _time);
        _validator = new StorytellerTokenValidator(_keyProvider, options, Options.Create(_exchangeOptions), _time, _logger);
    }

    public static TheoryData<string?> ActsThatAreNotOneObjectWithAStringSub => new()
    {
        { null },
        { "\"mcp\"" },
        { "[{\"sub\":\"mcp\"}]" },
        { "{}" },
        { "{\"sub\":42}" },
        { "{\"sub\":\" \"}" },
        { "{\"iss\":\"https://mcp.example\"}" },
    };

    public static TheoryData<string?> RestrictionsThatAreNotKnown => new()
    {
        { null },
        { "null" },
        { "\"NoSecretReveal\"" },
        { "[\"NoSecretReveal\",\"NoWrites\"]" },
        { "[\"nosecretreveal\"]" },
        { "[1]" },
        { "[null]" },
        { "[[\"NoSecretReveal\"]]" },
    };

    public static TheoryData<string> OtherTokenUses => new()
    {
        { "\"machine\"" },
        { "\"User\"" },
        { "\"\"" },
        { "1" },
        { "null" },
        { "[\"user\"]" },
    };

    [Fact]
    public async Task IssuedUserToken_IsTheUserOfTheAccount_ActingThroughTheActor()
    {
        var issued = await _issuer.IssueUserAsync(UserRequest());

        var result = await _validator.ValidateAsync(issued.AccessToken);

        result.ShouldNotBeNull();
        result.IsMachine.ShouldBeFalse();
        result.MachineId.ShouldBeNull();
        result.CredentialKind.ShouldBeNull();
        result.Organization.ShouldBeNull();
        result.Project.ShouldBeNull();
        result.Claims.Select(claim => (claim.Type, claim.Value)).ShouldBe(
        [
            ("sub", AccountId),
            ("scp", UserScopes.Impersonation),
            ("scp", "Annotation.Read"),
        ]);
        var delegation = result.Delegation.ShouldNotBeNull();
        delegation.ActorId.ShouldBe(ActorId);
        delegation.Restrictions.ShouldBe([ActorRestrictions.NoSecretReveal]);
    }

    [Fact]
    public async Task IssuedUserToken_WithoutRestrictions_HasADelegationWithout()
    {
        _exchangeOptions.Actors[0].AllowSecrets = true;
        var issued = await _issuer.IssueUserAsync(UserRequest() with { Restrictions = [] });

        var result = await _validator.ValidateAsync(issued.AccessToken);

        result.ShouldNotBeNull().Delegation.ShouldNotBeNull().Restrictions.ShouldBeEmpty();
    }

    // A token issued before AllowSecrets was turned off: the actor's settings of now add NoSecretReveal to it.
    [Fact]
    public async Task UserToken_IssuedWithoutRestrictions_GetsThoseTheActorHasNow()
    {
        var issued = await _issuer.IssueUserAsync(UserRequest() with { Restrictions = [] });

        var result = await _validator.ValidateAsync(issued.AccessToken);

        result.ShouldNotBeNull().Delegation.ShouldNotBeNull().Restrictions.ShouldBe([ActorRestrictions.NoSecretReveal]);
    }

    // Loosening the actor's settings never widens a token already issued.
    [Fact]
    public async Task UserToken_KeepsItsRestrictions_WhenTheActorAllowsSecretsLater()
    {
        var issued = await _issuer.IssueUserAsync(UserRequest());
        _exchangeOptions.Actors[0].AllowSecrets = true;

        var result = await _validator.ValidateAsync(issued.AccessToken);

        result.ShouldNotBeNull().Delegation.ShouldNotBeNull().Restrictions.ShouldBe([ActorRestrictions.NoSecretReveal]);
    }

    // The payload the negative cases change, signed by the key: accepted as it is.
    [Fact]
    public async Task SignedUserPayload_IsAccepted()
    {
        var result = await _validator.ValidateAsync(await SignAsync(UserPayload()));

        result.ShouldNotBeNull().Delegation.ShouldNotBeNull().ActorId.ShouldBe(ActorId);
    }

    [Fact]
    public async Task Scope_GivesOneScpPerDistinctScope()
    {
        var payload = UserPayload();
        payload["scope"] = "User.Impersonation  Annotation.Read User.Impersonation";

        var result = await _validator.ValidateAsync(await SignAsync(payload));

        result.ShouldNotBeNull().Claims.Where(claim => claim.Type == "scp").Select(claim => claim.Value)
            .ShouldBe([UserScopes.Impersonation, "Annotation.Read"]);
    }

    // The token's scopes count only within those the actor may carry now, as its restrictions count with the actor's:
    // a scope taken from the actor leaves its tokens at once, and one the actor never had is dropped.
    [Fact]
    public async Task Scope_IsNarrowedToTheScopesTheActorHasNow()
    {
        var payload = UserPayload();
        payload["scope"] = "User.Impersonation Annotation.Read Configuration.Read";

        var full = await _validator.ValidateAsync(await SignAsync(payload));
        full.ShouldNotBeNull().Claims.Where(claim => claim.Type == "scp").Select(claim => claim.Value)
            .ShouldBe([UserScopes.Impersonation, "Annotation.Read"]);

        _exchangeOptions.Actors[0].Scopes = [];
        var narrowed = await _validator.ValidateAsync(await SignAsync(payload));
        narrowed.ShouldNotBeNull().Claims.Where(claim => claim.Type == "scp").Select(claim => claim.Value)
            .ShouldBe([UserScopes.Impersonation]);

        _exchangeOptions.Actors[0].Scopes = ["Annotation.Read"];
        var withoutImpersonation = await _validator.ValidateAsync(await SignAsync(payload));
        withoutImpersonation.ShouldNotBeNull().Claims.Where(claim => claim.Type == "scp").Select(claim => claim.Value)
            .ShouldBe(["Annotation.Read"], "without User.Impersonation the token simply carries fewer scopes");
    }

    // The actor is matched ordinally, like the exchange matches client_id.
    [Theory]
    [InlineData("other")]
    [InlineData("MCP")]
    [InlineData("mcp ")]
    public async Task UserToken_OfAnActorThatIsNotConfigured_IsRefused(string actorId)
    {
        var issued = await _issuer.IssueUserAsync(UserRequest() with { ActorId = actorId });

        (await _validator.ValidateAsync(issued.AccessToken)).ShouldBeNull();
        _logger.Entries.ShouldContain(entry => entry.Level == LogLevel.Information && entry.Message.Contains("is not in Auth:TokenExchange:Actors"));
    }

    // Removing the actor, or turning the exchange off, ends its tokens at once.
    [Fact]
    public async Task UserToken_OfARemovedActor_IsRefused()
    {
        var issued = await _issuer.IssueUserAsync(UserRequest());
        (await _validator.ValidateAsync(issued.AccessToken)).ShouldNotBeNull();

        _exchangeOptions.Actors = [new TokenExchangeActorOptions { Id = "mcp-staging" }];

        (await _validator.ValidateAsync(issued.AccessToken)).ShouldBeNull();
    }

    [Fact]
    public async Task UserToken_WhileTheExchangeIsDisabled_IsRefused()
    {
        var issued = await _issuer.IssueUserAsync(UserRequest());
        _exchangeOptions.Enabled = false;

        (await _validator.ValidateAsync(issued.AccessToken)).ShouldBeNull();
        _logger.Entries.ShouldContain(entry => entry.Message.Contains("Auth:TokenExchange:Enabled is off"));
    }

    [Theory]
    [MemberData(nameof(ActsThatAreNotOneObjectWithAStringSub))]
    public async Task UserToken_WhoseActIsNotOneObjectWithAStringSub_IsRefused(string? act)
    {
        var payload = UserPayload();
        Set(payload, "act", act);

        (await _validator.ValidateAsync(await SignAsync(payload))).ShouldBeNull();
    }

    // RFC 8693 chains of actors are refused; the exchange never issues them.
    [Fact]
    public async Task UserToken_WithANestedAct_IsRefused()
    {
        var payload = UserPayload();
        payload["act"] = new JsonObject { ["sub"] = ActorId, ["act"] = new JsonObject { ["sub"] = "other" } };

        (await _validator.ValidateAsync(await SignAsync(payload))).ShouldBeNull();
        _logger.Entries.ShouldContain(entry => entry.Message.Contains("no nested act"));
    }

    // A restriction the API does not know could not be enforced, so the token is refused rather than the restriction lost.
    [Theory]
    [MemberData(nameof(RestrictionsThatAreNotKnown))]
    public async Task UserToken_WithRestrictionsThatAreNotKnown_IsRefused(string? restrictions)
    {
        var payload = UserPayload();
        Set(payload, "restrictions", restrictions);

        (await _validator.ValidateAsync(await SignAsync(payload))).ShouldBeNull();
    }

    [Fact]
    public async Task RepeatedRestriction_IsOne()
    {
        var payload = UserPayload();
        payload["restrictions"] = new JsonArray(ActorRestrictions.NoSecretReveal, ActorRestrictions.NoSecretReveal);

        var result = await _validator.ValidateAsync(await SignAsync(payload));

        result.ShouldNotBeNull().Delegation.ShouldNotBeNull().Restrictions.ShouldBe([ActorRestrictions.NoSecretReveal]);
    }

    // User tokens pass the same signature check as machine tokens: one whose restrictions were emptied after signing is
    // refused.
    [Fact]
    public async Task TamperedUserToken_IsRefused()
    {
        var issued = await _issuer.IssueUserAsync(UserRequest());
        var parts = issued.AccessToken.Split('.');
        var payload = JsonNode.Parse(Base64UrlEncoder.Decode(parts[1]))!.AsObject();
        payload["restrictions"] = new JsonArray();
        var tampered = $"{parts[0]}.{Base64UrlEncoder.Encode(payload.ToJsonString())}.{parts[2]}";

        (await _validator.ValidateAsync(tampered)).ShouldBeNull();
    }

    // Two claims with one name are refused, even when the last one would pass: this reader and the token handler must
    // never see different values.
    [Theory]
    [InlineData("\"restrictions\":[]")]
    [InlineData("\"restrictions\":[\"NoSecretReveal\"]")]
    [InlineData("\"act\":{\"sub\":\"mcp\"}")]
    [InlineData("\"token_use\":\"user\"")]
    public async Task UserToken_WithTwoClaimsOfOneName_IsRefused(string duplicate)
    {
        var json = UserPayload().ToJsonString();
        var payload = $"{json[..^1]},{duplicate}}}";

        (await _validator.ValidateAsync(await SignAsync(payload))).ShouldBeNull();
        _logger.Entries.ShouldContain(entry => entry.Message.Contains("not a JSON object of distinct claims"));
    }

    // The check runs before token_use picks the shape, so machine tokens get it too.
    [Fact]
    public async Task MachineToken_WithTwoClaimsOfOneName_IsRefused()
    {
        var json = MachinePayload().ToJsonString();
        var payload = $"{json[..^1]},\"organization\":\"org1\"}}";

        (await _validator.ValidateAsync(await SignAsync(payload))).ShouldBeNull();
        _logger.Entries.ShouldContain(entry => entry.Message.Contains("not a JSON object of distinct claims"));
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("project")]
    [InlineData("annotation_key")]
    public async Task UserToken_WithAClaimOfAMachineToken_IsRefused(string claim)
    {
        var payload = UserPayload();
        payload[claim] = "org1";

        (await _validator.ValidateAsync(await SignAsync(payload))).ShouldBeNull();
    }

    [Theory]
    [InlineData("sub", null)]
    [InlineData("sub", "\" \"")]
    [InlineData("sub", "42")]
    [InlineData("scope", null)]
    [InlineData("scope", "\"\"")]
    [InlineData("scope", "[\"User.Impersonation\"]")]
    public async Task UserToken_WithoutASubOrAScope_IsRefused(string claim, string? value)
    {
        var payload = UserPayload();
        Set(payload, claim, value);

        (await _validator.ValidateAsync(await SignAsync(payload))).ShouldBeNull();
    }

    [Theory]
    [MemberData(nameof(OtherTokenUses))]
    public async Task OtherTokenUse_IsRefused(string tokenUse)
    {
        var user = UserPayload();
        Set(user, "token_use", tokenUse);
        var machine = MachinePayload();
        Set(machine, "token_use", tokenUse);

        (await _validator.ValidateAsync(await SignAsync(user))).ShouldBeNull();
        (await _validator.ValidateAsync(await SignAsync(machine))).ShouldBeNull();
    }

    // Only user tokens name an actor, whatever form act has.
    [Theory]
    [InlineData("{\"sub\":\"mcp\"}")]
    [InlineData("\"mcp\"")]
    [InlineData("null")]
    public async Task MachineToken_WithAnAct_IsRefused(string act)
    {
        var payload = MachinePayload();
        Set(payload, "act", act);

        (await _validator.ValidateAsync(await SignAsync(payload))).ShouldBeNull();
    }

    [Fact]
    public async Task MachineToken_IsTheFederatedMachineAsBefore()
    {
        var issued = await _issuer.IssueAsync(new StorytellerTokenRequest
        {
            Organization = "org1",
            Project = "proj1",
            MachineId = MachineId,
            Scopes = ["Annotation.Read"],
            AnnotationKey = "unit.service",
            FederationIssuer = "github",
            FederationSubject = "repo:acme/app:ref:refs/heads/main",
        });

        var result = await _validator.ValidateAsync(issued.AccessToken);

        result.ShouldNotBeNull();
        result.IsMachine.ShouldBeTrue();
        result.MachineId.ShouldBe(MachineId);
        result.CredentialKind.ShouldBe(MachineCredentialKind.Federated);
        result.Delegation.ShouldBeNull();
        result.Claims.Select(claim => (claim.Type, claim.Value)).ShouldBe(
        [
            ("azp", MachineId),
            ("sub", MachineId),
            ("roles", "Annotation.Read"),
            ("annotationKey", "unit.service"),
        ]);
    }

    [Fact]
    public void Validator_MayReturnUsers()
    {
        _validator.ShouldBeAssignableTo<IUserTokenIssuerValidator>();
    }

    // A value written as JSON; null removes the claim.
    private static void Set(JsonObject payload, string claim, string? json)
    {
        if (json is null)
        {
            payload.Remove(claim);
            return;
        }

        payload[claim] = JsonNode.Parse(json);
    }

    private StorytellerUserTokenRequest UserRequest()
    {
        return new StorytellerUserTokenRequest
        {
            AccountId = AccountId,
            ActorId = ActorId,
            Scopes = [UserScopes.Impersonation, "Annotation.Read"],
            Restrictions = [ActorRestrictions.NoSecretReveal],
            MaxLifetime = TimeSpan.FromMinutes(10),
            NotAfter = _time.Now.AddHours(1),
        };
    }

    // The claims of IssueUserAsync.
    private JsonObject UserPayload()
    {
        var now = _time.GetUtcNow().ToUnixTimeSeconds();
        return new JsonObject
        {
            ["iss"] = Issuer,
            ["aud"] = Audience,
            ["sub"] = AccountId,
            ["token_use"] = "user",
            ["scope"] = UserScopes.Impersonation,
            ["act"] = new JsonObject { ["sub"] = ActorId },
            ["client_id"] = ActorId,
            ["restrictions"] = new JsonArray(ActorRestrictions.NoSecretReveal),
            ["jti"] = Guid.NewGuid().ToString("D"),
            ["iat"] = now,
            ["nbf"] = now,
            ["exp"] = now + 600,
        };
    }

    // The claims of IssueAsync.
    private JsonObject MachinePayload()
    {
        var now = _time.GetUtcNow().ToUnixTimeSeconds();
        return new JsonObject
        {
            ["iss"] = Issuer,
            ["aud"] = Audience,
            ["sub"] = MachineId,
            ["client_id"] = MachineId,
            ["scope"] = "Annotation.Read",
            ["organization"] = "org1",
            ["project"] = "proj1",
            ["iat"] = now,
            ["nbf"] = now,
            ["exp"] = now + 900,
        };
    }

    // Signs the payload as written, with the stored signing key, as only someone holding it could.
    private Task<string> SignAsync(JsonObject payload)
    {
        return SignAsync(payload.ToJsonString());
    }

    // The raw payload, for JSON a JsonObject cannot hold (two claims with one name).
    private async Task<string> SignAsync(string payloadJson)
    {
        var credentials = await _keyProvider.GetSigningCredentialsAsync();
        var header = new JsonObject { ["alg"] = SecurityAlgorithms.RsaSha256, ["kid"] = credentials.Key.KeyId, ["typ"] = "at+jwt" };
        var unsigned = $"{Base64UrlEncoder.Encode(header.ToJsonString())}.{Base64UrlEncoder.Encode(payloadJson)}";
        var provider = credentials.Key.CryptoProviderFactory.CreateForSigning(credentials.Key, SecurityAlgorithms.RsaSha256);

        try
        {
            return $"{unsigned}.{Base64UrlEncoder.Encode(provider.Sign(Encoding.ASCII.GetBytes(unsigned)))}";
        }
        finally
        {
            credentials.Key.CryptoProviderFactory.ReleaseSignatureProvider(provider);
        }
    }
}
