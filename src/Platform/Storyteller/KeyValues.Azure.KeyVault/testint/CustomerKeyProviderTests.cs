using System.Security.Cryptography;
using _42.Platform.Storyteller.KeyValues.Protection;
using Azure.Security.KeyVault.Keys;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace _42.Platform.Storyteller.KeyValues.Azure.KeyVault.IntegrationTests;

// Lowkey Vault plays the customer's vault; the emulator credential stands in for the cross-tenant identity.
[Collection("LowkeyVault")]
public class CustomerKeyProviderTests(LowkeyVaultFixture vault)
{
    private static readonly KeyValueScope Scope = new("org-tests", "byok-project");

    private static IOptions<KeyValueOptions> Options() => Microsoft.Extensions.Options.Options.Create(new KeyValueOptions
    {
        Protection = new KeyValueProtectionOptions { Emulator = true },
    });

    [Fact]
    public void SetupInfo_NamesTheApplicationAndTheRole()
    {
        var provider = new CustomerKeyProvider(Options());

        var setup = provider.GetSetupInfo();

        setup.ClientId.Should().Be("emulator");
        setup.ConsentUrl.Should().Contain("adminconsent").And.Contain("client_id=emulator");
        setup.RequiredRole.Should().Be("Key Vault Crypto Service Encryption User");
        provider.Protection.Should().Be(KeyValueProtection.Byok);
    }

    [Fact]
    public async Task Verify_AcceptsAUsableKeyAndRejectsOneWithoutTheOperations()
    {
        var provider = new CustomerKeyProvider(Options());
        var usable = await CreateCustomerKeyAsync(KeyOperation.WrapKey, KeyOperation.UnwrapKey, KeyOperation.Encrypt, KeyOperation.Decrypt);
        var signOnly = await CreateCustomerKeyAsync(KeyOperation.Sign, KeyOperation.Verify);

        var accepted = await provider.VerifyAsync(usable);
        var rejected = await provider.VerifyAsync(signOnly);
        var missing = await provider.VerifyAsync(usable with { KeyUri = $"{vault.VaultUri}keys/does-not-exist" });

        accepted.IsValid.Should().BeTrue(accepted.Error);
        accepted.CurrentKeyVersion.Should().NotBeNullOrEmpty();
        rejected.IsValid.Should().BeFalse();
        rejected.Error.Should().Contain("wrapKey");
        missing.IsValid.Should().BeFalse();
        missing.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task WrapAndUnwrap_UseTheCustomerKeyAndFollowItsRotation()
    {
        var provider = new CustomerKeyProvider(Options());
        var settings = await CreateCustomerKeyAsync(KeyOperation.WrapKey, KeyOperation.UnwrapKey, KeyOperation.Encrypt, KeyOperation.Decrypt);
        var policy = new KeyValuePolicy { Byok = settings with { VerifiedAt = DateTimeOffset.UtcNow } };
        var dataKey = RandomNumberGenerator.GetBytes(32);

        var info = await provider.EnsureKeyAsync(Scope, policy);
        var wrapped = await provider.WrapAsync(Scope, policy, dataKey);

        info.KeyId.Should().Be(settings.KeyUri);
        wrapped.KeyId.Should().StartWith(settings.KeyUri);
        (await provider.UnwrapAsync(Scope, policy, KeyVaultKeyProviderTests.ToEncryptedValue(wrapped))).Should().Equal(dataKey);

        // the customer rotates in their own vault; the platform only picks the new version up
        var name = new KeyVaultKeyIdentifier(new Uri(settings.KeyUri)).Name;
        await vault.CreateClient().RotateKeyAsync(name);
        var afterRotation = await provider.GetKeyInfoAsync(Scope, policy);
        afterRotation.CurrentKeyVersion.Should().NotBe(info.CurrentKeyVersion);
        (await provider.UnwrapAsync(Scope, policy, KeyVaultKeyProviderTests.ToEncryptedValue(wrapped))).Should().Equal(dataKey, "the old version still unwraps");

        await provider.Invoking(p => p.RotateAsync(Scope, policy)).Should().ThrowAsync<InvalidInputException>();
    }

    [Fact]
    public async Task Unwrap_RejectsAnEnvelopeOfAnotherKey()
    {
        var provider = new CustomerKeyProvider(Options());
        var settings = await CreateCustomerKeyAsync(KeyOperation.WrapKey, KeyOperation.UnwrapKey, KeyOperation.Encrypt, KeyOperation.Decrypt);
        var other = await CreateCustomerKeyAsync(KeyOperation.WrapKey, KeyOperation.UnwrapKey, KeyOperation.Encrypt, KeyOperation.Decrypt);
        var wrappedByOther = await provider.WrapAsync(Scope, new KeyValuePolicy { Byok = other }, RandomNumberGenerator.GetBytes(32));

        var act = () => provider.UnwrapAsync(Scope, new KeyValuePolicy { Byok = settings }, KeyVaultKeyProviderTests.ToEncryptedValue(wrappedByOther));

        (await act.Should().ThrowAsync<FailedDependencyException>()).Which.ErrorCode.Should().Be(ErrorCodes.CustomerKeyUnavailable);
    }

    [Fact]
    public async Task DisabledKey_MakesTheLevelUnavailable()
    {
        var provider = new CustomerKeyProvider(Options());
        var settings = await CreateCustomerKeyAsync(KeyOperation.WrapKey, KeyOperation.UnwrapKey, KeyOperation.Encrypt, KeyOperation.Decrypt);
        var policy = new KeyValuePolicy { Byok = settings };
        var wrapped = await provider.WrapAsync(Scope, policy, RandomNumberGenerator.GetBytes(32));

        var client = vault.CreateClient();
        var key = await client.GetKeyAsync(new KeyVaultKeyIdentifier(new Uri(settings.KeyUri)).Name);
        await client.UpdateKeyPropertiesAsync(new KeyProperties(key.Value.Id) { Enabled = false });

        var info = () => provider.GetKeyInfoAsync(Scope, policy);
        (await info.Should().ThrowAsync<FailedDependencyException>()).Which.ErrorCode.Should().Be(ErrorCodes.CustomerKeyUnavailable);

        var unwrap = () => provider.UnwrapAsync(Scope, policy, KeyVaultKeyProviderTests.ToEncryptedValue(wrapped));
        await unwrap.Should().ThrowAsync<StorytellerException>("a disabled key refuses the operation");
        (await provider.VerifyAsync(settings)).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task WithoutByokSettings_TheLevelIsNotConfiguredForTheProject()
    {
        var provider = new CustomerKeyProvider(Options());

        var act = () => provider.WrapAsync(Scope, new KeyValuePolicy(), RandomNumberGenerator.GetBytes(32));

        (await act.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.ByokNotConfigured);
    }

    private async Task<ByokSettings> CreateCustomerKeyAsync(params KeyOperation[] operations)
    {
        var client = vault.CreateClient();
        var name = $"customer-kek-{Guid.NewGuid():N}";
        var options = new CreateRsaKeyOptions(name) { KeySize = 2048 };

        foreach (var operation in operations)
        {
            options.KeyOperations.Add(operation);
        }

        await client.CreateRsaKeyAsync(options);

        return new ByokSettings
        {
            KeyUri = new Uri(vault.VaultUri, $"keys/{name}").ToString(),
            TenantId = Guid.NewGuid().ToString(),
        };
    }
}
