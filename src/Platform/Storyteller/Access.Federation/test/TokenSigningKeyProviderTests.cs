using System.Security.Cryptography;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Access.Federation.UnitTests;

public class TokenSigningKeyProviderTests
{
    private readonly ManualTimeProvider _time = new();
    private readonly CountingSigningKeyStore _store = new();
    private readonly FederationOptions _options = new() { Issuer = "urn:storyteller:test", Audience = "urn:storyteller:test:api" };

    [Fact]
    public async Task Issuing_WithoutAStoredKey_CreatesAnRsaKeyNamedByItsThumbprint()
    {
        var credentials = await CreateProvider().GetSigningCredentialsAsync();

        credentials.Algorithm.ShouldBe(SecurityAlgorithms.RsaSha256);
        var stored = _store.Key.ShouldNotBeNull();
        credentials.Key.KeyId.ShouldBe(stored.Kid);
        stored.Algorithm.ShouldBe("RS256");

        using var rsa = RSA.Create();
        rsa.ImportPkcs8PrivateKey(stored.PrivateKeyPkcs8, out _);
        rsa.KeySize.ShouldBe(2048);
        stored.Kid.ShouldBe(Base64UrlEncoder.Encode(new RsaSecurityKey(rsa.ExportParameters(false)).ComputeJwkThumbprint()));
    }

    [Fact]
    public async Task Issuing_WhenAnotherInstanceCreatedTheKeyFirst_ReadsAndUsesThatKey()
    {
        var winner = CreateKey();
        _store.Winner = winner;

        var credentials = await CreateProvider().GetSigningCredentialsAsync();

        credentials.Key.KeyId.ShouldBe(winner.Kid);
        _store.Creates.ShouldBe(1);
        _store.Reads.ShouldBe(2);
    }

    [Fact]
    public async Task Issuing_WithAStoredKey_UsesItAndCachesIt()
    {
        var stored = CreateKey();
        _store.Key = stored;
        var provider = CreateProvider();

        (await provider.GetSigningCredentialsAsync()).Key.KeyId.ShouldBe(stored.Kid);
        (await provider.GetSigningCredentialsAsync()).Key.KeyId.ShouldBe(stored.Kid);

        _store.Reads.ShouldBe(1);
        _store.Creates.ShouldBe(0);
    }

    [Fact]
    public async Task Issuing_WithBootstrapDisabled_IsUnavailable()
    {
        _options.SigningKeyAutoBootstrap = false;

        await Should.ThrowAsync<TokenSigningKeyUnavailableException>(() => CreateProvider().GetSigningCredentialsAsync());

        _store.Creates.ShouldBe(0);
        _store.Key.ShouldBeNull();
    }

    [Fact]
    public async Task Issuing_WhenTheStoreCannotBeRead_IsUnavailable()
    {
        _store.ReadError = new InvalidOperationException("Cosmos DB is down");

        var exception = await Should.ThrowAsync<TokenSigningKeyUnavailableException>(() => CreateProvider().GetSigningCredentialsAsync());

        exception.InnerException.ShouldBeSameAs(_store.ReadError);
        _store.Creates.ShouldBe(0);
    }

    [Fact]
    public async Task Issuing_AfterTheStoredKeyWasDeleted_WithBootstrapDisabled_IsUnavailable_AndTheOldKeyIsRefused()
    {
        _options.SigningKeyAutoBootstrap = false;
        var stored = CreateKey();
        _store.Key = stored;
        var provider = CreateProvider(issuingCacheDuration: TimeSpan.Zero);
        (await provider.GetSigningCredentialsAsync()).Key.KeyId.ShouldBe(stored.Kid);

        // The emergency rotation, once the issuing cache has expired: no key, not the deleted one.
        _store.Key = null;

        await Should.ThrowAsync<TokenSigningKeyUnavailableException>(() => provider.GetSigningCredentialsAsync());
        await Should.ThrowAsync<TokenSigningKeyUnavailableException>(() => provider.GetSigningCredentialsAsync());
        (await provider.GetValidationKeyAsync(stored.Kid)).ShouldBeNull();
        _store.Creates.ShouldBe(0);
    }

    [Fact]
    public async Task Issuing_WhenTheStoredKeyIsReplacedByOneOfAnotherAlgorithm_IsUnavailable()
    {
        var stored = CreateKey();
        _store.Key = stored;
        var provider = CreateProvider(issuingCacheDuration: TimeSpan.Zero);
        (await provider.GetSigningCredentialsAsync()).Key.KeyId.ShouldBe(stored.Kid);

        _store.Key = stored with { Algorithm = "ES256" };

        await Should.ThrowAsync<TokenSigningKeyUnavailableException>(() => provider.GetSigningCredentialsAsync());
        (await provider.GetValidationKeyAsync(stored.Kid)).ShouldBeNull();
    }

    [Fact]
    public async Task Issuing_WithBootstrapDisabled_UsesAKeyStoredLater_OnTheNextExchange()
    {
        _options.SigningKeyAutoBootstrap = false;
        var provider = CreateProvider();
        await Should.ThrowAsync<TokenSigningKeyUnavailableException>(() => provider.GetSigningCredentialsAsync());

        var stored = CreateKey();
        _store.Key = stored;

        (await provider.GetSigningCredentialsAsync()).Key.KeyId.ShouldBe(stored.Kid);
    }

    [Fact]
    public async Task Issuing_WhenTheStoreCannotBeReloaded_KeepsTheLoadedKey()
    {
        var stored = CreateKey();
        _store.Key = stored;
        var provider = CreateProvider(issuingCacheDuration: TimeSpan.Zero);
        (await provider.GetSigningCredentialsAsync()).Key.KeyId.ShouldBe(stored.Kid);

        _store.ReadError = new InvalidOperationException("Cosmos DB is down");

        (await provider.GetSigningCredentialsAsync()).Key.KeyId.ShouldBe(stored.Kid);
        _store.Reads.ShouldBe(2);
    }

    [Fact]
    public async Task Validating_NeverCreatesAKey_AndReadsTheStoreAtMostEvery30Seconds()
    {
        var provider = CreateProvider();

        (await provider.GetValidationKeyAsync("forged")).ShouldBeNull();
        (await provider.GetValidationKeyAsync("forged-again")).ShouldBeNull();
        _store.Reads.ShouldBe(1);

        _time.Advance(TimeSpan.FromSeconds(30));
        (await provider.GetValidationKeyAsync("forged")).ShouldBeNull();

        _store.Reads.ShouldBe(2);
        _store.Creates.ShouldBe(0);
        _store.Key.ShouldBeNull();
    }

    [Fact]
    public async Task Validating_PicksUpAKeyAnotherInstanceCreated_AfterTheReloadInterval()
    {
        var validating = CreateProvider();
        (await validating.GetValidationKeyAsync("anything")).ShouldBeNull();

        var issued = await CreateProvider().GetSigningCredentialsAsync();
        (await validating.GetValidationKeyAsync(issued.Key.KeyId)).ShouldBeNull();

        _time.Advance(TimeSpan.FromSeconds(30));
        (await validating.GetValidationKeyAsync(issued.Key.KeyId)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Validating_TheKeyThisInstanceSignsWith_NeedsNoRead()
    {
        var provider = CreateProvider();
        var issued = await provider.GetSigningCredentialsAsync();
        var reads = _store.Reads;

        var key = await provider.GetValidationKeyAsync(issued.Key.KeyId);

        key.ShouldNotBeNull();
        key.KeyId.ShouldBe(issued.Key.KeyId);
        ((RsaSecurityKey)key).PrivateKeyStatus.ShouldBe(PrivateKeyStatus.DoesNotExist);
        _store.Reads.ShouldBe(reads);
    }

    [Fact]
    public async Task Validating_AnUnknownKid_IsRefusedWithoutIoBetweenReloads()
    {
        var stored = CreateKey();
        _store.Key = stored;
        var provider = CreateProvider();

        (await provider.GetValidationKeyAsync(stored.Kid)).ShouldNotBeNull();
        (await provider.GetValidationKeyAsync("unknown-1")).ShouldBeNull();
        (await provider.GetValidationKeyAsync("unknown-2")).ShouldBeNull();
        (await provider.GetValidationKeyAsync(stored.Kid)).ShouldNotBeNull();
        _store.Reads.ShouldBe(1);

        _time.Advance(TimeSpan.FromSeconds(31));
        (await provider.GetValidationKeyAsync("unknown-3")).ShouldBeNull();
        _store.Reads.ShouldBe(2);
    }

    [Fact]
    public async Task Validating_AfterTheStoredKeyWasDeleted_RefusesItsTokensOnceReloaded()
    {
        var stored = CreateKey();
        _store.Key = stored;
        var provider = CreateProvider();
        (await provider.GetValidationKeyAsync(stored.Kid)).ShouldNotBeNull();

        // The emergency rotation.
        _store.Key = null;
        _time.Advance(TokenSigningKeyProvider.IssuingCacheDuration);

        (await provider.GetValidationKeyAsync(stored.Kid)).ShouldBeNull();
    }

    [Fact]
    public async Task Validating_WhenTheStoreCannotBeRead_IsUnavailable_WithoutAReadPerRequest()
    {
        _store.ReadError = new InvalidOperationException("Cosmos DB is down");
        var provider = CreateProvider();

        await Should.ThrowAsync<BearerKeyRetrievalException>(() => provider.GetValidationKeyAsync("kid"));
        await Should.ThrowAsync<BearerKeyRetrievalException>(() => provider.GetValidationKeyAsync("kid"));
        _store.Reads.ShouldBe(1);

        var stored = CreateKey();
        _store.ReadError = null;
        _store.Key = stored;
        _time.Advance(TokenSigningKeyProvider.ValidationReloadInterval);

        (await provider.GetValidationKeyAsync(stored.Kid)).ShouldNotBeNull();
    }

    [Fact]
    public async Task Validating_WhenTheStoreCannotBeReloaded_KeepsTheLoadedKey()
    {
        var stored = CreateKey();
        _store.Key = stored;
        var provider = CreateProvider();
        (await provider.GetValidationKeyAsync(stored.Kid)).ShouldNotBeNull();

        _store.ReadError = new InvalidOperationException("Cosmos DB is down");
        _time.Advance(TokenSigningKeyProvider.IssuingCacheDuration);

        (await provider.GetValidationKeyAsync(stored.Kid)).ShouldNotBeNull();
    }

    private static TokenSigningKey CreateKey()
    {
        using var rsa = RSA.Create(2048);
        return new TokenSigningKey(TokenSigningKeyProvider.ComputeKid(rsa), "RS256", rsa.ExportPkcs8PrivateKey(), DateTimeOffset.UtcNow);
    }

    private TokenSigningKeyProvider CreateProvider(TimeSpan? issuingCacheDuration = null)
    {
        return new TokenSigningKeyProvider(
            _store,
            Options.Create(_options),
            _time,
            NullLogger<TokenSigningKeyProvider>.Instance,
            issuingCacheDuration ?? TokenSigningKeyProvider.IssuingCacheDuration);
    }
}
