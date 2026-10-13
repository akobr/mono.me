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

    // A test-only key pair generated for this fixture; it protects nothing.
    private const string TypeScriptSdkPrivateKeyPem = """
        -----BEGIN PRIVATE KEY-----
        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
        q4TVFlYP7StiXA21pT6PiA==
        -----END PRIVATE KEY-----
        """;

    // encryptForRecipients("from the TypeScript SDK", [{ id: "api", publicKey }], "acme/billing/default/db.password")
    private const string TypeScriptSdkDocument = """
        {"protected":"eyJlbmMiOiJBMjU2R0NNIiwiY3R5IjoidGV4dC9wbGFpbiIsIjJzIjoiYWNtZS9iaWxsaW5nL2RlZmF1bHQvZGIucGFzc3dvcmQifQ","recipients":[{"header":{"alg":"RSA-OAEP-256","kid":"api"},"encrypted_key":"c1UttwnIgsH3IF6A0YU9yUTXnJpX4Cznvm6mejIZoARpqVquuRWpm-2sL3mU-g2IqKT1xjjm0F6AjtWmgjMIqayvTggsenKEu_BR8ICEMQZfwGSoexZv26D51Y5GC_hjF0Vp10IXkh1aYNzFtcgDpOV_sXN9N6rc3jYo2ycLHfDTKspoFeAVI1lhIw_bFnREI6NojYSV7Ot8Yy8IsW8yEzAO8i65GslkdquGHNtLbGr-Oqbs9zS_I-Bk8YReOLxFDWcwfS6S8moOkGBrDQmYP2x4WYWN_jnKzhinRXYfgPcM9xOZzbXrK57I6q5z_1-0LsXx0SX3Ol8yoZCVgfOdWZp0pNTqDsY2g-Y-8-9kjeZ_s2byiUXmqdhHsNBMlDiaqlODHjku2uXpEDhAsOJFsvmFE6qXR8mKiXcqnXh-_DcmT4b86z089kFlJ62di--IKjvWoJ67cRJv_YnmLBNosOoRP_ruzsSbnL3bTSmsyuGN1BH79Oe9pBc91L3-U800"}],"iv":"ts7bQV3DTvJWA2MP","ciphertext":"SDocrvrdpghIymI_4krr_tilyOqgO4E","tag":"Pprjyt0WB1y4xFrbMl20Wg"}
        """;

    private static RsaJsonWebKey ToModel(RsaPublicJwk jwk) => new() { N = jwk.N, E = jwk.E };

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

    // A document written by the TypeScript SDK (@42for.net/2splatform.sdk/hyok, WebCrypto) for a 3072-bit test key:
    // the server validates it and the C# library decrypts it, so the two implementations stay interoperable.
    [Fact]
    public void Document_FromTheTypeScriptSdk_ValidatesAndDecrypts()
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(TypeScriptSdkPrivateKeyPem);
        var identity = new KeyValueIdentity("acme", "billing", "default", "db.password", 1);
        var recipients = new[] { new ClientEncryptionRecipient { Id = "api", Name = "api", PublicKey = ToModel(RsaPublicJwk.FromRsa(rsa)), CreatedAt = DateTimeOffset.UnixEpoch, CreatedBy = "test" } };

        var document = JweDocument.Parse(TypeScriptSdkDocument);
        var validated = ClientEncryptedValueValidator.Validate(TypeScriptSdkDocument, identity, recipients);

        document.Place.Should().Be("acme/billing/default/db.password");
        document.ContentType.Should().Be("text/plain");
        validated.Should().Equal("api");
        ClientSecretDecryptor.Decrypt(TypeScriptSdkDocument, "api", rsa).Should().Be("from the TypeScript SDK");
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
