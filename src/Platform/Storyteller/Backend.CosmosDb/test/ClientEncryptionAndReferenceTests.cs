#nullable enable

using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.KeyValues.ClientEncryption;
using _42.Platform.Storyteller.KeyValues.Protection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The JWE of client-encrypted values, its validation, and the shape of secret references. Mostly without Cosmos.
public class ClientEncryptionAndReferenceTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Org = TestConstants.Organization;
    private const string View = Constants.DefaultViewName;

    private static readonly KeyValueCaller Admin = KeyValueCaller.WithSecrets("account: admin");

    [Fact]
    public void Encrypt_ForSeveralRecipients_EachDecrypts_AndTamperingIsDetected()
    {
        using var first = RSA.Create(3072);
        using var second = RSA.Create(3072);
        using var stranger = RSA.Create(3072);
        var recipients = new[]
        {
            new ClientEncryptionRecipientKey("first", RsaPublicJwk.FromRsa(first)),
            new ClientEncryptionRecipientKey("second", RsaPublicJwk.FromRsa(second)),
        };

        var jwe = ClientSecretEncryptor.Encrypt("the value: ünïcödé", recipients, "acme/billing/default/db.password", "application/json");
        var document = JweDocument.Parse(jwe);

        document.Enc.Should().Be("A256GCM");
        document.Place.Should().Be("acme/billing/default/db.password");
        document.ContentType.Should().Be("application/json");
        document.Recipients.Should().HaveCount(2);
        document.Recipients.Should().OnlyContain(recipient => recipient.Algorithm == "RSA-OAEP-256");
        ClientSecretDecryptor.Decrypt(jwe, "first", first).Should().Be("the value: ünïcödé");
        ClientSecretDecryptor.Decrypt(jwe, "second", second).Should().Be("the value: ünïcödé");

        var wrongKey = () => ClientSecretDecryptor.Decrypt(jwe, "first", stranger);
        wrongKey.Should().Throw<CryptographicException>();
        var notARecipient = () => ClientSecretDecryptor.Decrypt(jwe, "third", first);
        notARecipient.Should().Throw<CryptographicException>();

        var tampered = jwe.Replace("\"ciphertext\":\"", "\"ciphertext\":\"AA", StringComparison.Ordinal);
        var tamperedRead = () => ClientSecretDecryptor.Decrypt(tampered, "first", first);
        tamperedRead.Should().Throw<CryptographicException>();

        var jwk = RsaPublicJwk.Parse(first.ExportSubjectPublicKeyInfoPem());
        jwk.Should().Be(RsaPublicJwk.FromRsa(first));
        RsaPublicJwk.Parse(jwk.ToJson()).Should().Be(jwk);
        jwk.ModulusBits.Should().Be(3072);
        var garbage = () => RsaPublicJwk.Parse("not a key");
        garbage.Should().Throw<FormatException>();
    }

    [Fact]
    public void Validation_ChecksPlaceEncryptionAndRecipients()
    {
        using var rsa = RSA.Create(3072);
        var identity = new KeyValueIdentity("acme", "billing", "default", "db.password", 1);
        var recipients = new[]
        {
            new ClientEncryptionRecipient { Id = "app", Name = "App", PublicKey = new RsaJsonWebKey { N = "x", E = "AQAB" }, IsEnabled = true, CreatedAt = DateTimeOffset.UtcNow, CreatedBy = "t" },
            new ClientEncryptionRecipient { Id = "old", Name = "Old", PublicKey = new RsaJsonWebKey { N = "x", E = "AQAB" }, IsEnabled = false, CreatedAt = DateTimeOffset.UtcNow, CreatedBy = "t" },
        };
        var key = new ClientEncryptionRecipientKey("app", RsaPublicJwk.FromRsa(rsa));
        var place = ClientEncryptedValueValidator.GetPlace(identity);

        ClientEncryptedValueValidator.Validate(ClientSecretEncryptor.Encrypt("v", [key], place), identity, recipients).Should().Equal("app");

        var otherPlace = () => ClientEncryptedValueValidator.Validate(ClientSecretEncryptor.Encrypt("v", [key], "acme/billing/default/other"), identity, recipients);
        otherPlace.Should().Throw<InvalidInputException>().Which.ErrorCode.Should().Be(ErrorCodes.InvalidClientEncryptedValue);

        var disabled = () => ClientEncryptedValueValidator.Validate(ClientSecretEncryptor.Encrypt("v", [new ClientEncryptionRecipientKey("old", RsaPublicJwk.FromRsa(rsa))], place), identity, recipients);
        disabled.Should().Throw<InvalidInputException>().Which.ErrorCode.Should().Be(ErrorCodes.UnknownRecipient);

        var notJson = () => ClientEncryptedValueValidator.Validate("plain text", identity, recipients);
        notJson.Should().Throw<InvalidInputException>().Which.ErrorCode.Should().Be(ErrorCodes.InvalidClientEncryptedValue);

        var noRecipients = () => ClientEncryptedValueValidator.Validate("""{"protected":"e30","recipients":[],"iv":"AA","ciphertext":"AA","tag":"AA"}""", identity, recipients);
        noRecipients.Should().Throw<InvalidInputException>();
    }

    [Theory]
    [InlineData("kv://my-vault/db-password", "https://my-vault.vault.azure.net/secrets/db-password")]
    [InlineData("kv://my-vault/db-password/0123456789abcdef0123456789abcdef", "https://my-vault.vault.azure.net/secrets/db-password/0123456789abcdef0123456789abcdef")]
    [InlineData("https://other.vault.azure.net/secrets/name", "https://other.vault.azure.net/secrets/name")]
    [InlineData("  https://other.vault.azure.net/secrets/name/  ", "https://other.vault.azure.net/secrets/name/")]
    public void References_AcceptTheShorthandAndFullUris(string value, string expected)
    {
        SecretReferences.Ensure(value, KeyValueContentTypes.SecretReference).ToString().Should().Be(expected);
    }

    [Theory]
    [InlineData("kv://vault")]
    [InlineData("kv://vault/name/notaversion")]
    [InlineData("http://vault.vault.azure.net/secrets/name")]
    [InlineData("https://vault.vault.azure.net/keys/name")]
    [InlineData("just text")]
    public void References_RejectOtherShapes(string value)
    {
        var act = () => SecretReferences.Ensure(value, KeyValueContentTypes.SecretReference);

        act.Should().Throw<InvalidInputException>().Which.ErrorCode.Should().Be(ErrorCodes.InvalidSecretReference);
    }

    [Fact]
    public void References_AcceptTheAppConfigurationFormat()
    {
        var uri = SecretReferences.Ensure("""{"uri":"https://vault.vault.azure.net/secrets/name"}""", KeyValueContentTypes.AppConfigurationKeyVaultReference);
        uri.ToString().Should().Be("https://vault.vault.azure.net/secrets/name");

        var noUri = () => SecretReferences.Ensure("""{"url":"x"}""", KeyValueContentTypes.AppConfigurationKeyVaultReference);
        noUri.Should().Throw<InvalidInputException>();
    }

    [Fact]
    public async Task References_AreStoredAsPublicPairsOnly()
    {
        var keyValues = Context.Services.GetRequiredService<IKeyValueService>();
        var project = $"kv-ref-{Guid.NewGuid().ToString("N")[..12]}";

        var stored = await keyValues.SetAsync(Org, project, View, "db.password", new KeyValueWrite
        {
            Protection = KeyValueProtection.Public,
            Value = "kv://customer-vault/db-password",
            ContentType = KeyValueContentTypes.SecretReference,
        }, Admin);
        stored.IsReference.Should().BeTrue();

        var invalid = () => keyValues.SetAsync(Org, project, View, "db.password", new KeyValueWrite { Value = "nope", ContentType = KeyValueContentTypes.SecretReference }, Admin);
        (await invalid.Should().ThrowAsync<InvalidInputException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvalidSecretReference);

        var secretReference = () => keyValues.SetAsync(Org, project, View, "other", new KeyValueWrite
        {
            Protection = KeyValueProtection.Standard,
            Value = "kv://customer-vault/x",
            ContentType = KeyValueContentTypes.SecretReference,
        }, Admin);
        (await secretReference.Should().ThrowAsync<InvalidInputException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvalidSecretReference);

        var lookup = await keyValues.LookupAsync(Org, project, View, "db.password", includeSecrets: false);
        lookup!.Value.Should().Be("kv://customer-vault/db-password", "a reference binds as the text it is; the platform never resolves it");
    }
}
