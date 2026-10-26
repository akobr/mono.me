using System.Security.Cryptography;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Access.Federation.UnitTests;

// The public keys for the JWKS: the keys validation accepts, through its reload gate.
public class TokenSigningKeyProviderJwksTests
{
    private readonly ManualTimeProvider _time = new();
    private readonly CountingSigningKeyStore _store = new();
    private readonly FederationOptions _options = new() { Issuer = "urn:storyteller:test", Audience = "urn:storyteller:test:api" };

    [Fact]
    public async Task Jwks_PublishesThePublicHalfOfTheStoredKey()
    {
        using var rsa = RSA.Create(2048);
        var stored = new TokenSigningKey(TokenSigningKeyProvider.ComputeKid(rsa), "RS256", rsa.ExportPkcs8PrivateKey(), DateTimeOffset.UtcNow);
        _store.Key = stored;

        var key = (await CreateProvider().GetPublicJsonWebKeysAsync()).ShouldHaveSingleItem();

        var parameters = rsa.ExportParameters(includePrivateParameters: false);
        key.ShouldBe(new StorytellerJsonWebKey(
            stored.Kid,
            "RSA",
            Base64UrlEncoder.Encode(parameters.Modulus),
            Base64UrlEncoder.Encode(parameters.Exponent),
            "RS256",
            "sig"));

        // The kid is the RFC 7638 thumbprint of exactly this n and e.
        var jwk = new JsonWebKey { Kty = key.Kty, N = key.N, E = key.E };
        Base64UrlEncoder.Encode(jwk.ComputeJwkThumbprint()).ShouldBe(key.Kid);
    }

    [Fact]
    public async Task Jwks_NeverCreatesAKey_AndReadsTheStoreAtMostEvery30Seconds()
    {
        var provider = CreateProvider();

        (await provider.GetPublicJsonWebKeysAsync()).ShouldBeEmpty();
        (await provider.GetPublicJsonWebKeysAsync()).ShouldBeEmpty();
        _store.Reads.ShouldBe(1);

        _time.Advance(TokenSigningKeyProvider.ValidationReloadInterval);
        (await provider.GetPublicJsonWebKeysAsync()).ShouldBeEmpty();

        _store.Reads.ShouldBe(2);
        _store.Creates.ShouldBe(0);
        _store.Key.ShouldBeNull();
    }

    [Fact]
    public async Task Jwks_OfAnInstanceThatSigns_NeedsNoRead()
    {
        var provider = CreateProvider();
        var credentials = await provider.GetSigningCredentialsAsync();
        var reads = _store.Reads;

        (await provider.GetPublicJsonWebKeysAsync()).ShouldHaveSingleItem().Kid.ShouldBe(credentials.Key.KeyId);
        _store.Reads.ShouldBe(reads);
    }

    // A forged kid and the JWKS share one read per reload interval.
    [Fact]
    public async Task Jwks_SharesTheReloadGateWithValidation()
    {
        _store.Key = CreateKey();
        var provider = CreateProvider();

        (await provider.GetValidationKeyAsync("forged")).ShouldBeNull();
        (await provider.GetPublicJsonWebKeysAsync()).ShouldHaveSingleItem().Kid.ShouldBe(_store.Key.Kid);
        (await provider.GetValidationKeyAsync("forged-again")).ShouldBeNull();

        _store.Reads.ShouldBe(1);
    }

    [Fact]
    public async Task Jwks_PicksUpAKeyAnotherInstanceCreated_AfterTheReloadInterval()
    {
        var publishing = CreateProvider();
        (await publishing.GetPublicJsonWebKeysAsync()).ShouldBeEmpty();

        var issued = await CreateProvider().GetSigningCredentialsAsync();
        (await publishing.GetPublicJsonWebKeysAsync()).ShouldBeEmpty();

        _time.Advance(TokenSigningKeyProvider.ValidationReloadInterval);
        (await publishing.GetPublicJsonWebKeysAsync()).ShouldHaveSingleItem().Kid.ShouldBe(issued.Key.KeyId);
    }

    [Fact]
    public async Task Jwks_AfterTheStoredKeyWasDeleted_IsEmptyOnceReloaded()
    {
        _store.Key = CreateKey();
        var provider = CreateProvider();
        (await provider.GetPublicJsonWebKeysAsync()).ShouldHaveSingleItem();

        // The emergency rotation.
        _store.Key = null;
        _time.Advance(TokenSigningKeyProvider.IssuingCacheDuration);

        (await provider.GetPublicJsonWebKeysAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Jwks_WhenTheStoreCannotBeRead_IsUnavailable_WithoutAReadPerRequest()
    {
        _store.ReadError = new InvalidOperationException("Cosmos DB is down");
        var provider = CreateProvider();

        await Should.ThrowAsync<BearerKeyRetrievalException>(() => provider.GetPublicJsonWebKeysAsync());
        await Should.ThrowAsync<BearerKeyRetrievalException>(() => provider.GetPublicJsonWebKeysAsync());
        await Should.ThrowAsync<BearerKeyRetrievalException>(() => provider.GetValidationKeyAsync("kid"));
        _store.Reads.ShouldBe(1);
    }

    [Fact]
    public async Task Jwks_WhenTheStoreCannotBeReloaded_KeepsTheLoadedKey()
    {
        _store.Key = CreateKey();
        var provider = CreateProvider();
        (await provider.GetPublicJsonWebKeysAsync()).ShouldHaveSingleItem();

        _store.ReadError = new InvalidOperationException("Cosmos DB is down");
        _time.Advance(TokenSigningKeyProvider.IssuingCacheDuration);

        (await provider.GetPublicJsonWebKeysAsync()).ShouldHaveSingleItem();
        _store.Reads.ShouldBe(2);
    }

    private static TokenSigningKey CreateKey()
    {
        using var rsa = RSA.Create(2048);
        return new TokenSigningKey(TokenSigningKeyProvider.ComputeKid(rsa), "RS256", rsa.ExportPkcs8PrivateKey(), DateTimeOffset.UtcNow);
    }

    private TokenSigningKeyProvider CreateProvider()
    {
        return new TokenSigningKeyProvider(_store, Options.Create(_options), _time, NullLogger<TokenSigningKeyProvider>.Instance);
    }
}
