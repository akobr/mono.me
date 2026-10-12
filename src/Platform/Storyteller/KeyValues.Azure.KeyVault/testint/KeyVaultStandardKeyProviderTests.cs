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
public class KeyVaultStandardKeyProviderTests(LowkeyVaultFixture vault)
{
    private static KeyValueScope NewScope() => new("org-tests", $"project-{Guid.NewGuid():N}");

    private static IOptions<KeyValueOptions> Options(int keySize = 2048) => Microsoft.Extensions.Options.Options.Create(new KeyValueOptions
    {
        Protection = new KeyValueProtectionOptions
        {
            Emulator = true,
            Standard = new KeyVaultKeyOptions { KeySize = keySize },
        },
    });

    [Fact]
    public void KeyName_IsStablePerProjectAndValidForKeyVault()
    {
        var scope = new KeyValueScope("acme", "billing");

        var name = KeyVaultStandardKeyProvider.GetKeyName("kek-", scope);

        name.Should().Be(KeyVaultStandardKeyProvider.GetKeyName("kek-", scope));
        name.Should().NotBe(KeyVaultStandardKeyProvider.GetKeyName("kek-", new KeyValueScope("acme", "shop")));
        name.Should().MatchRegex("^kek-[0-9a-f]{32}$");
    }

    [Fact]
    public async Task EnsureKey_CreatesOneKeyPerProject()
    {
        var client = vault.CreateClient();
        var provider = new KeyVaultStandardKeyProvider(client, Options());
        var scope = NewScope();
        var name = KeyVaultStandardKeyProvider.GetKeyName("kek-", scope);

        var first = await provider.EnsureKeyAsync(scope);
        var second = await provider.EnsureKeyAsync(scope);
        var fresh = await new KeyVaultStandardKeyProvider(client, Options()).EnsureKeyAsync(scope);

        first.Should().Be(second).And.Be(fresh).And.EndWith($"/keys/{name}");
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
    public async Task WrapAndUnwrap_RoundTrip_AlsoWithAnOlderKeyVersion()
    {
        var client = vault.CreateClient();
        var provider = new KeyVaultStandardKeyProvider(client, Options());
        var scope = NewScope();
        var dataKey = RandomNumberGenerator.GetBytes(32);

        var wrapped = await provider.WrapAsync(scope, dataKey);
        wrapped.Algorithm.Should().Be(KeyVaultStandardKeyProvider.WrapAlgorithmName);
        wrapped.KeyId.Should().Contain("/keys/kek-");
        new KeyVaultKeyIdentifier(new Uri(wrapped.KeyId)).Version.Should().NotBeNullOrEmpty("the wrap records the key version");

        var envelope = ToEncryptedValue(wrapped);
        (await provider.UnwrapAsync(scope, envelope)).Should().Equal(dataKey);

        await client.RotateKeyAsync(KeyVaultStandardKeyProvider.GetKeyName("kek-", scope));
        var wrappedAfterRotation = await provider.WrapAsync(scope, dataKey);
        wrappedAfterRotation.KeyId.Should().NotBe(wrapped.KeyId, "new wraps use the new key version");

        (await provider.UnwrapAsync(scope, envelope)).Should().Equal(dataKey, "the old version still unwraps old envelopes");
        (await provider.UnwrapAsync(scope, ToEncryptedValue(wrappedAfterRotation))).Should().Equal(dataKey);
    }

    [Fact]
    public async Task Unwrap_RejectsAnotherVaultOrAlgorithm()
    {
        var provider = new KeyVaultStandardKeyProvider(vault.CreateClient(), Options());
        var scope = NewScope();
        var wrapped = await provider.WrapAsync(scope, RandomNumberGenerator.GetBytes(32));

        var otherVault = ToEncryptedValue(wrapped) with { KeyId = "https://other.vault.azure.net/keys/kek-x/1" };
        var otherAlgorithm = ToEncryptedValue(wrapped) with { Algorithm = "RSA1_5" };

        await provider.Invoking(p => p.UnwrapAsync(scope, otherVault)).Should().ThrowAsync<KeyValueIntegrityException>();
        await provider.Invoking(p => p.UnwrapAsync(scope, otherAlgorithm)).Should().ThrowAsync<KeyValueIntegrityException>();
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
        var provider = new KeyVaultStandardKeyProvider(client, Options());

        var act = () => provider.WrapAsync(NewScope(), RandomNumberGenerator.GetBytes(32));

        var failure = await act.Should().ThrowAsync<ServiceUnavailableException>();
        failure.Which.ErrorCode.Should().Be(ErrorCodes.KeyEncryptionUnavailable);
        failure.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(7));
    }

    [Fact]
    public async Task EnvelopeProtector_EndToEndWithTheVault()
    {
        var provider = new KeyVaultStandardKeyProvider(vault.CreateClient(), Options());
        using var protector = new EnvelopeValueProtector([provider], Options());
        var identity = new KeyValueIdentity("org-tests", $"project-{Guid.NewGuid():N}", "default", "db.password", 3);

        var stored = await protector.ProtectAsync(identity, KeyValueProtection.Standard, "hunter2", "text/plain");

        stored.Value.Should().BeNull();
        stored.Encrypted.Should().NotBeNull();
        stored.Length.Should().Be(7);
        (await protector.UnprotectAsync(identity, stored)).Should().Be("hunter2");
        await protector.Invoking(p => p.UnprotectAsync(identity with { Version = 4 }, stored)).Should().ThrowAsync<KeyValueIntegrityException>();
    }

    private static EncryptedValue ToEncryptedValue(WrappedDataKey wrapped) => new()
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
