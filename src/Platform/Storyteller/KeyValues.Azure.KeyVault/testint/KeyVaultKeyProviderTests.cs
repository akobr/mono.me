using System.Net;
using System.Security.Cryptography;
using _42.Platform.Storyteller.KeyValues.Protection;
using Azure.Core.Pipeline;
using Azure.Security.KeyVault.Keys;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace _42.Platform.Storyteller.KeyValues.Azure.KeyVault.IntegrationTests;

[Collection("LowkeyVault")]
public class KeyVaultKeyProviderTests(LowkeyVaultFixture vault)
{
    private static readonly KeyValuePolicy Policy = new();

    private static KeyValueScope NewScope() => new("org-tests", $"project-{Guid.NewGuid():N}");

    private static IOptions<KeyValueOptions> Options() => Microsoft.Extensions.Options.Options.Create(new KeyValueOptions
    {
        Protection = new KeyValueProtectionOptions { Emulator = true },
    });

    [Fact]
    public void KeyName_IsStablePerProjectAndValidForKeyVault()
    {
        var scope = new KeyValueScope("acme", "billing");

        var name = KeyVaultKeyProvider.GetKeyName("kek-", scope);

        name.Should().Be(KeyVaultKeyProvider.GetKeyName("kek-", scope));
        name.Should().NotBe(KeyVaultKeyProvider.GetKeyName("kek-", new KeyValueScope("acme", "shop")));
        name.Should().MatchRegex("^kek-[0-9a-f]{32}$");
    }

    [Fact]
    public async Task EnsureKey_CreatesOneKeyPerProject()
    {
        var client = vault.CreateClient();
        var provider = KeyVaultKeyProvider.CreateStandard(client, Options());
        var scope = NewScope();
        var name = KeyVaultKeyProvider.GetKeyName("kek-", scope);

        var first = await provider.EnsureKeyAsync(scope, Policy);
        var second = await provider.EnsureKeyAsync(scope, Policy);
        var fresh = await KeyVaultKeyProvider.CreateStandard(client, Options()).EnsureKeyAsync(scope, Policy);

        first.KeyId.Should().Be(second.KeyId).And.Be(fresh.KeyId).And.EndWith($"/keys/{name}");
        first.CurrentKeyVersion.Should().NotBeNullOrEmpty().And.Be(fresh.CurrentKeyVersion);
        first.NextRotationAt.Should().BeAfter(DateTimeOffset.UtcNow.AddDays(300));
        var key = await client.GetKeyAsync(name);
        key.Value.KeyType.Should().Be(KeyType.Rsa);
        key.Value.KeyOperations.Should().Contain([KeyOperation.WrapKey, KeyOperation.UnwrapKey]);
        key.Value.KeyOperations.Should().NotContain(KeyOperation.Sign);

        var versions = new List<KeyProperties>();
        await foreach (var version in client.GetPropertiesOfKeyVersionsAsync(name))
        {
            versions.Add(version);
        }

        versions.Should().ContainSingle("a second ensure creates no version");
    }

    [Fact]
    public async Task WrapAndUnwrap_RoundTrip_AlsoWithAnOlderKeyVersionAfterARotation()
    {
        var provider = KeyVaultKeyProvider.CreateStandard(vault.CreateClient(), Options());
        var scope = NewScope();
        var dataKey = RandomNumberGenerator.GetBytes(32);

        var wrapped = await provider.WrapAsync(scope, Policy, dataKey);
        wrapped.Algorithm.Should().Be(KeyVaultKeyProvider.WrapAlgorithmName);
        wrapped.KeyId.Should().Contain("/keys/kek-");
        var firstVersion = new KeyVaultKeyIdentifier(new Uri(wrapped.KeyId)).Version;
        firstVersion.Should().NotBeNullOrEmpty("the wrap records the key version");
        (await provider.GetKeyInfoAsync(scope, Policy)).CurrentKeyVersion.Should().Be(firstVersion);

        var envelope = ToEncryptedValue(wrapped);
        (await provider.UnwrapAsync(scope, Policy, envelope)).Should().Equal(dataKey);

        var rotated = await provider.RotateAsync(scope, Policy);
        rotated.CurrentKeyVersion.Should().NotBe(firstVersion);
        (await provider.GetKeyInfoAsync(scope, Policy)).CurrentKeyVersion.Should().Be(rotated.CurrentKeyVersion);

        var wrappedAfterRotation = await provider.WrapAsync(scope, Policy, dataKey);
        new KeyVaultKeyIdentifier(new Uri(wrappedAfterRotation.KeyId)).Version.Should().Be(rotated.CurrentKeyVersion, "new wraps use the new key version");

        (await provider.UnwrapAsync(scope, Policy, envelope)).Should().Equal(dataKey, "the old version still unwraps old envelopes");
        (await provider.UnwrapAsync(scope, Policy, ToEncryptedValue(wrappedAfterRotation))).Should().Equal(dataKey);
    }

    [Fact]
    public async Task Unwrap_RejectsAnotherVaultOrAlgorithm()
    {
        var provider = KeyVaultKeyProvider.CreateStandard(vault.CreateClient(), Options());
        var scope = NewScope();
        var wrapped = await provider.WrapAsync(scope, Policy, RandomNumberGenerator.GetBytes(32));

        var otherVault = ToEncryptedValue(wrapped) with { KeyId = "https://other.vault.azure.net/keys/kek-x/1" };
        var otherAlgorithm = ToEncryptedValue(wrapped) with { Algorithm = "RSA1_5" };

        await provider.Invoking(p => p.UnwrapAsync(scope, Policy, otherVault)).Should().ThrowAsync<KeyValueIntegrityException>();
        await provider.Invoking(p => p.UnwrapAsync(scope, Policy, otherAlgorithm)).Should().ThrowAsync<KeyValueIntegrityException>();
    }

    [Fact]
    public async Task Hsm_InEmulatorMode_UsesSoftwareKeysAndReportsTheBackend()
    {
        var client = vault.CreateClient();
        var shared = KeyVaultKeyProvider.CreateHsm(client, new Dictionary<string, KeyClient>(), Options());
        var dedicated = KeyVaultKeyProvider.CreateHsm(null, new Dictionary<string, KeyClient>(StringComparer.OrdinalIgnoreCase) { ["acme"] = client }, Options());
        var scope = new KeyValueScope("acme", $"project-{Guid.NewGuid():N}");
        var dataKey = RandomNumberGenerator.GetBytes(32);

        shared.Protection.Should().Be(KeyValueProtection.Hsm);
        shared.HardwareProtected.Should().BeFalse("Lowkey Vault has no HSM keys");
        shared.GetBackend(scope).Should().Be(HsmBackend.KeyVaultPremium);
        dedicated.GetBackend(scope).Should().Be(HsmBackend.ManagedHsm);
        dedicated.GetBackend(new KeyValueScope("other", "p")).Should().Be(HsmBackend.KeyVaultPremium);

        var wrapped = await dedicated.WrapAsync(scope, Policy, dataKey);
        (await dedicated.UnwrapAsync(scope, Policy, ToEncryptedValue(wrapped))).Should().Equal(dataKey);

        var noVault = () => dedicated.WrapAsync(new KeyValueScope("other", "p"), Policy, dataKey);
        (await noVault.Should().ThrowAsync<FeatureNotConfiguredException>()).Which.ErrorCode.Should().Be(ErrorCodes.ProtectionNotConfigured);
    }

    [Fact]
    public async Task Throttling_BecomesServiceUnavailable()
    {
        var options = new KeyClientOptions
        {
            Transport = new HttpClientTransport(new ThrottlingHandler()),
        };
        options.Retry.MaxRetries = 0;
        var client = new KeyClient(new Uri("https://throttled.vault.azure.net/"), new StaticCredential(), options);
        var provider = KeyVaultKeyProvider.CreateStandard(client, Options());

        var act = () => provider.WrapAsync(NewScope(), Policy, RandomNumberGenerator.GetBytes(32));

        var failure = await act.Should().ThrowAsync<ServiceUnavailableException>();
        failure.Which.ErrorCode.Should().Be(ErrorCodes.KeyEncryptionUnavailable);
        failure.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public async Task EnvelopeProtector_EndToEndWithTheVault()
    {
        var standard = KeyVaultKeyProvider.CreateStandard(vault.CreateClient(), Options());
        var hsm = KeyVaultKeyProvider.CreateHsm(vault.CreateClient(), new Dictionary<string, KeyClient>(), Options());
        using var protector = new EnvelopeValueProtector([standard, hsm], new InMemoryPolicyStore(), Options());
        var identity = new KeyValueIdentity("org-tests", $"project-{Guid.NewGuid():N}", "default", "db.password", 3);

        var stored = await protector.ProtectAsync(identity, KeyValueProtection.Standard, "hunter2", "text/plain");
        var storedHsm = await protector.ProtectAsync(identity with { Version = 4 }, KeyValueProtection.Hsm, "hunter3", null);

        stored.Value.Should().BeNull();
        stored.Encrypted.Should().NotBeNull();
        stored.Length.Should().Be(7);
        storedHsm.Protection.Should().Be(KeyValueProtection.Hsm);
        (await protector.UnprotectAsync(identity, stored)).Should().Be("hunter2");
        (await protector.UnprotectAsync(identity with { Version = 4 }, storedHsm)).Should().Be("hunter3");

        // a version not seen before (the plaintext cache is per version id): the envelope of version 3 does not open as version 5
        await protector.Invoking(p => p.UnprotectAsync(identity with { Version = 5 }, stored)).Should().ThrowAsync<KeyValueIntegrityException>();
        await protector.Invoking(p => p.ProtectAsync(identity, KeyValueProtection.Byok, "x", null)).Should().ThrowAsync<FeatureNotConfiguredException>();
    }

    internal static EncryptedValue ToEncryptedValue(WrappedDataKey wrapped) => new()
    {
        KeyId = wrapped.KeyId,
        Algorithm = wrapped.Algorithm,
        WrappedKey = System.Buffers.Text.Base64Url.EncodeToString(wrapped.Value),
        Nonce = string.Empty,
        Ciphertext = string.Empty,
    };

    private sealed class ThrottlingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.Add("Retry-After", "7");
            return Task.FromResult(response);
        }
    }

    private sealed class StaticCredential : global::Azure.Core.TokenCredential
    {
        public override global::Azure.Core.AccessToken GetToken(global::Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new("token", DateTimeOffset.MaxValue);

        public override ValueTask<global::Azure.Core.AccessToken> GetTokenAsync(global::Azure.Core.TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(GetToken(requestContext, cancellationToken));
    }
}
