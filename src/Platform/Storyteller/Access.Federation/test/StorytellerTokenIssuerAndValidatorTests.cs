using System.Text;
using System.Text.Json;
using _42.Platform.Storyteller.Accessing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Access.Federation.UnitTests;

public sealed class StorytellerTokenIssuerAndValidatorTests
{
    private const string Issuer = "urn:storyteller:2s.test";
    private const string Audience = "urn:storyteller:2s.test:api";
    private const string MachineId = "6f1c2a8e-4c1b-4d4b-9a51-0f6b0d9a3c11";

    private readonly ManualTimeProvider _time = new();
    private readonly CountingSigningKeyStore _store = new();
    private readonly FederationOptions _options = new() { Issuer = Issuer, Audience = Audience };
    private readonly TokenSigningKeyProvider _keyProvider;
    private readonly StorytellerTokenIssuer _issuer;
    private readonly StorytellerTokenValidator _validator;

    public StorytellerTokenIssuerAndValidatorTests()
    {
        var options = Options.Create(_options);
        _keyProvider = new TokenSigningKeyProvider(_store, options, _time, NullLogger<TokenSigningKeyProvider>.Instance);
        _issuer = new StorytellerTokenIssuer(_keyProvider, options, _time);
        _validator = new StorytellerTokenValidator(_keyProvider, options, _time, NullLogger<StorytellerTokenValidator>.Instance);
    }

    [Fact]
    public async Task IssuedToken_IsAnRfc9068AccessTokenOfTheMachine()
    {
        var issued = await _issuer.IssueAsync(Request());

        issued.ExpiresIn.ShouldBe(900);
        issued.ExpiresAt.ShouldBe(DateTimeOffset.FromUnixTimeSeconds(_time.GetUtcNow().ToUnixTimeSeconds()).AddMinutes(15));

        var token = new JsonWebToken(issued.AccessToken);
        token.Typ.ShouldBe("at+jwt");
        token.Alg.ShouldBe("RS256");
        token.Kid.ShouldBe(_store.Key!.Kid);
        token.Issuer.ShouldBe(Issuer);
        token.Audiences.ShouldBe([Audience]);
        token.Subject.ShouldBe(MachineId);
        token.GetPayloadValue<string>("client_id").ShouldBe(MachineId);
        token.GetPayloadValue<string>("scope").ShouldBe("Annotation.Read Configuration.Read");
        token.GetPayloadValue<string>("organization").ShouldBe("org1");
        token.GetPayloadValue<string>("project").ShouldBe("proj1");
        token.GetPayloadValue<string>("annotation_key").ShouldBe("unit.service");
        token.GetPayloadValue<string>("federation_issuer").ShouldBe("github");
        token.GetPayloadValue<string>("federation_subject").ShouldBe("repo:acme/app:ref:refs/heads/main");
        Guid.TryParse(token.Id, out _).ShouldBeTrue();
        (token.ValidTo - token.IssuedAt).ShouldBe(TimeSpan.FromMinutes(15));
        token.ValidFrom.ShouldBe(token.IssuedAt);
    }

    [Fact]
    public async Task IssuedToken_WithoutAnAnnotationKey_HasNoSuchClaim()
    {
        var issued = await _issuer.IssueAsync(Request() with { AnnotationKey = null });

        new JsonWebToken(issued.AccessToken).TryGetPayloadValue<string>("annotation_key", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task RoundTrip_GivesAFederatedMachineWithTheClaimsOfAnApiKeyMachine()
    {
        var issued = await _issuer.IssueAsync(Request());

        var result = await _validator.ValidateAsync(issued.AccessToken);

        result.ShouldNotBeNull();
        result.IsMachine.ShouldBeTrue();
        result.MachineId.ShouldBe(MachineId);
        result.CredentialKind.ShouldBe(MachineCredentialKind.Federated);
        result.Organization.ShouldBe("org1");
        result.Project.ShouldBe("proj1");
        result.Claims.Select(claim => (claim.Type, claim.Value)).ShouldBe(
        [
            ("azp", MachineId),
            ("sub", MachineId),
            ("roles", "Annotation.Read"),
            ("roles", "Configuration.Read"),
            ("annotationKey", "unit.service"),
        ]);
    }

    [Fact]
    public void Validator_OwnsOnlyTheStorytellerIssuer()
    {
        _validator.CanValidate(Issuer).ShouldBeTrue();
        _validator.OwnsIssuer(Issuer).ShouldBeTrue();
        _validator.OwnsIssuer(Issuer + "/").ShouldBeFalse();
        _validator.CanValidate("https://login.microsoftonline.com/tenant/v2.0").ShouldBeFalse();
        ((IMachineTokenValidator)_validator).OwnsIssuer("urn:storyteller:other").ShouldBeFalse();
    }

    [Theory]
    [InlineData("at+jwt", true)]
    [InlineData("application/at+jwt", true)]
    [InlineData("AT+JWT", true)]
    [InlineData("JWT", false)]
    [InlineData(null, false)]
    public async Task OnlyAccessTokenTypes_AreAccepted(string? type, bool accepted)
    {
        var token = await SignAsync(Payload(), type);

        ((await _validator.ValidateAsync(token)) is not null).ShouldBe(accepted);
    }

    [Fact]
    public async Task ExpiredToken_IsRefused_AfterTheSkew()
    {
        var issued = await _issuer.IssueAsync(Request());

        _time.Advance(TimeSpan.FromMinutes(15) + TimeSpan.FromSeconds(29));
        (await _validator.ValidateAsync(issued.AccessToken)).ShouldNotBeNull();

        _time.Advance(TimeSpan.FromSeconds(2));
        (await _validator.ValidateAsync(issued.AccessToken)).ShouldBeNull();
    }

    [Fact]
    public async Task TokenNotYetValid_IsRefused()
    {
        var issued = await _issuer.IssueAsync(Request());

        _time.Advance(TimeSpan.FromMinutes(-1));

        (await _validator.ValidateAsync(issued.AccessToken)).ShouldBeNull();
    }

    [Fact]
    public async Task ForeignIssuerOrAudience_IsRefused()
    {
        var foreignIssuer = Payload();
        foreignIssuer["iss"] = "urn:storyteller:other";
        var foreignAudience = Payload();
        foreignAudience["aud"] = "urn:storyteller:other:api";
        var trailingSlash = Payload();
        trailingSlash["aud"] = Audience + "/";

        (await _validator.ValidateAsync(await SignAsync(foreignIssuer))).ShouldBeNull();
        (await _validator.ValidateAsync(await SignAsync(foreignAudience))).ShouldBeNull();
        (await _validator.ValidateAsync(await SignAsync(trailingSlash))).ShouldBeNull();
    }

    [Fact]
    public async Task TamperedToken_IsRefused()
    {
        var issued = await _issuer.IssueAsync(Request());
        var parts = issued.AccessToken.Split('.');
        var payload = JsonSerializer.Deserialize<Dictionary<string, object>>(Base64UrlEncoder.Decode(parts[1]))!;
        payload["scope"] = "Annotation.Read Annotation.Write Secrets.Read Secrets.Write";
        var tampered = $"{parts[0]}.{Base64UrlEncoder.Encode(JsonSerializer.Serialize(payload))}.{parts[2]}";

        (await _validator.ValidateAsync(tampered)).ShouldBeNull();
    }

    [Fact]
    public async Task TokenSignedWithRs384_IsRefused()
    {
        (await _validator.ValidateAsync(await SignAsync(Payload(), algorithm: SecurityAlgorithms.RsaSha384))).ShouldBeNull();
    }

    [Theory]
    [InlineData("organization")]
    [InlineData("project")]
    [InlineData("scope")]
    [InlineData("sub")]
    public async Task TokenWithoutAMachineClaim_IsRefused(string claim)
    {
        var payload = Payload();
        payload.Remove(claim);

        (await _validator.ValidateAsync(await SignAsync(payload))).ShouldBeNull();
    }

    [Fact]
    public async Task TokenOfAnUnknownKey_IsRefused_AndReadsTheStoreAtMostEvery30Seconds()
    {
        await _issuer.IssueAsync(Request());
        using var forger = TestSigningKey.Rsa("forged");
        var reads = _store.Reads;

        // The key was just loaded for issuing, which counts as the last read.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await _validator.ValidateAsync(Forge(forger, Payload()))).ShouldBeNull();
        }

        _store.Reads.ShouldBe(reads);

        _time.Advance(TokenSigningKeyProvider.ValidationReloadInterval);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            (await _validator.ValidateAsync(Forge(forger, Payload()))).ShouldBeNull();
        }

        _store.Reads.ShouldBe(reads + 1);
        _store.Creates.ShouldBe(1);
    }

    [Fact]
    public async Task TokenWithTheKidOfTheSigningKeyButAnotherSignature_IsRefused()
    {
        await _issuer.IssueAsync(Request());
        using var forger = TestSigningKey.Rsa(_store.Key!.Kid);

        (await _validator.ValidateAsync(Forge(forger, Payload()))).ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("a.b.c")]
    public async Task Garbage_IsRefused(string token)
    {
        (await _validator.ValidateAsync(token)).ShouldBeNull();
    }

    private static StorytellerTokenRequest Request()
    {
        return new StorytellerTokenRequest
        {
            Organization = "org1",
            Project = "proj1",
            MachineId = MachineId,
            Scopes = ["Annotation.Read", "Configuration.Read"],
            AnnotationKey = "unit.service",
            FederationIssuer = "github",
            FederationSubject = "repo:acme/app:ref:refs/heads/main",
        };
    }

    private static string Forge(TestSigningKey key, Dictionary<string, object> payload)
    {
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            TokenType = "at+jwt",
            Claims = payload,
            SigningCredentials = new SigningCredentials(key.SecurityKey, SecurityAlgorithms.RsaSha256),
        });
    }

    private Dictionary<string, object> Payload()
    {
        var now = _time.GetUtcNow().ToUnixTimeSeconds();
        return new Dictionary<string, object>
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

    // Signs with the stored signing key, as only someone holding it could.
    private async Task<string> SignAsync(Dictionary<string, object> payload, string? type = "at+jwt", string algorithm = SecurityAlgorithms.RsaSha256)
    {
        var credentials = await _keyProvider.GetSigningCredentialsAsync();
        var handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false };

        if (type is null)
        {
            var header = new { alg = algorithm, kid = credentials.Key.KeyId };
            var unsigned = $"{TestSigningKey.Encode(header)}.{TestSigningKey.Encode(payload)}";
            var provider = credentials.Key.CryptoProviderFactory.CreateForSigning(credentials.Key, algorithm);
            return $"{unsigned}.{Base64UrlEncoder.Encode(provider.Sign(Encoding.ASCII.GetBytes(unsigned)))}";
        }

        return handler.CreateToken(new SecurityTokenDescriptor
        {
            TokenType = type,
            Claims = payload,
            SigningCredentials = new SigningCredentials(credentials.Key, algorithm),
        });
    }
}
