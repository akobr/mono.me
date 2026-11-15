#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The values of the access audit records (no Cosmos DB): capped, compact, and never a secret; and the records as
// AccessAuditRecords builds them from the clock and the retention.
public class AccessAuditValuesTests
{
    private const string Modulus = "sXchDaQebHnPiGvyDOAT4saGEUetSyo9MKLOoWFsueri23bOdgWp4Dy1WlUzewbgBHod5pcM9H95GQRV3JDXboIRROSBigeC5yjU1hGz";

    [Fact]
    public void Cap_AValueOfTheLimit_StaysWhole()
    {
        var value = new string('a', AccessAuditValues.MaxLength);

        AccessAuditValues.Cap(value).Should().BeSameAs(value);
    }

    [Fact]
    public void Cap_ALongerValue_IsCutToTheLimitAndEndsWithTheStartOfItsSha256()
    {
        var value = string.Concat(Enumerable.Range(0, 1000).Select(index => $"{index:D5}."));
        var sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16].ToLowerInvariant();
        var suffix = $"… (truncated, sha256 {sha256})";

        var capped = AccessAuditValues.Cap(value);

        capped.Should().HaveLength(AccessAuditValues.MaxLength);
        capped.Should().EndWith(suffix);
        capped.Should().StartWith(value[..(AccessAuditValues.MaxLength - suffix.Length)]);
    }

    [Fact]
    public void TextAndJson_AreCappedToo()
    {
        var description = new string('d', AccessAuditValues.MaxLength + 1);

        AccessAuditValues.Text(null).Should().BeNull();
        AccessAuditValues.Text("Billing.").Should().Be("Billing.");
        AccessAuditValues.Text(description).Should().HaveLength(AccessAuditValues.MaxLength).And.Contain("(truncated, sha256 ");
        AccessAuditValues.Json(new { Description = description }).Should().HaveLength(AccessAuditValues.MaxLength).And.StartWith("{\"Description\":\"ddd");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(AccountRole.None, null)]
    [InlineData(AccountRole.Reader, "Reader")]
    [InlineData(AccountRole.ContributorWithSecrets, "ContributorWithSecrets")]
    [InlineData(AccountRole.Owner, "Owner")]
    public void Role_IsTheNameOfTheRole_AndNoneIsNoRole(AccountRole? role, string? expected)
    {
        AccessAuditValues.Role(role).Should().Be(expected);
    }

    [Fact]
    public void Machine_IsCompactJsonOfWhatDecidesItsAccess_WithoutEmptyValues()
    {
        var full = AccessAuditValues.Machine(MachineCredentialKind.CertificateAndApiKey, [MachineScope.AnnotationRead, MachineScope.SecretsRead], "unit.billing", true, "AB12");
        var bare = AccessAuditValues.Machine(MachineCredentialKind.ApiKey, null, string.Empty, false, null);

        full.Should().Be("{\"CredentialKind\":\"CertificateAndApiKey\",\"Scopes\":[\"AnnotationRead\",\"SecretsRead\"],\"AnnotationKey\":\"unit.billing\",\"UsesSharedCertificate\":true,\"CertificateThumbprint\":\"AB12\"}");
        bare.Should().Be("{\"CredentialKind\":\"ApiKey\",\"Scopes\":[]}");
    }

    [Fact]
    public void Machine_HasNoPlaceForASecret()
    {
        // what a machine value can hold at all: no key, client secret, mask, hash, certificate or password
        using var document = JsonDocument.Parse(AccessAuditValues.Machine(MachineCredentialKind.CertificateAndApiKey, [MachineScope.AnnotationRead], "unit", true, "AB12"));

        document.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "CredentialKind",
            "Scopes",
            "AnnotationKey",
            "UsesSharedCertificate",
            "CertificateThumbprint");
    }

    [Fact]
    public void KeyValuePolicy_KeyVersionsHealthVerificationAndRewrap_ChangeNoView()
    {
        var policy = Policy();
        var maintained = policy with
        {
            Standard = new ProtectionKeyInfo { KeyId = "kek-standard", CurrentKeyVersion = "v7", NextRotationAt = DateTimeOffset.UtcNow, CheckedAt = DateTimeOffset.UtcNow },
            Hsm = policy.Hsm! with { Key = new ProtectionKeyInfo { KeyId = "kek-hsm", CurrentKeyVersion = "v3" } },
            Byok = policy.Byok! with
            {
                VerifiedAt = DateTimeOffset.UtcNow,
                CurrentKeyVersion = "v9",
                Health = CustomerKeyHealth.Unavailable,
                LastCheckedAt = DateTimeOffset.UtcNow,
                LastError = "The key is disabled.",
            },
            Rewrap = new RewrapStatus { Protection = KeyValueProtection.Byok, TargetKeyVersion = "v9", StartedAt = DateTimeOffset.UtcNow, Processed = 4, Remaining = 2 },
            Version = policy.Version + 5,
            Modified = AuditStamp.Of(AuditActor.System, DateTimeOffset.UtcNow),
        };

        AccessAuditValues.KeyValuePolicy(maintained).Should().Be(AccessAuditValues.KeyValuePolicy(policy));
    }

    [Fact]
    public void KeyValuePolicy_EveryPartThatDecidesAccess_ChangesTheView()
    {
        var policy = Policy();
        var recipient = policy.Recipients[0];
        var changes = new Dictionary<string, KeyValuePolicy>
        {
            ["allowed protections"] = policy with { AllowedProtections = [KeyValueProtection.Public] },
            ["default protection"] = policy with { DefaultSecretProtection = KeyValueProtection.Hsm },
            ["mTLS for secrets"] = policy with { RequireMtlsForSecretDelivery = false },
            ["HSM rotation"] = policy with { Hsm = policy.Hsm! with { RotationInterval = TimeSpan.FromDays(30) } },
            ["customer key"] = policy with { Byok = policy.Byok! with { KeyUri = "https://other.vault.azure.net/keys/kek" } },
            ["customer tenant"] = policy with { Byok = policy.Byok! with { TenantId = "other-tenant" } },
            ["recipient name"] = policy with { Recipients = [recipient with { Name = "Other" }] },
            ["recipient disabled"] = policy with { Recipients = [recipient with { IsEnabled = false }] },
            ["recipient key"] = policy with { Recipients = [recipient with { PublicKey = recipient.PublicKey with { N = Modulus[..^1] + "x" } }] },
            ["recipient removed"] = policy with { Recipients = [] },
        };
        var view = AccessAuditValues.KeyValuePolicy(policy);

        foreach (var (change, changed) in changes)
        {
            AccessAuditValues.KeyValuePolicy(changed).Should().NotBe(view, $"a changed {change} decides who gets secrets");
        }
    }

    [Fact]
    public void KeyValuePolicy_KeepsTheRecipientsIdNameAndKeyThumbprint_NeverTheirKey()
    {
        using var view = JsonDocument.Parse(AccessAuditValues.KeyValuePolicy(Policy()));

        var recipient = view.RootElement.GetProperty("Recipients").EnumerateArray().Single();
        recipient.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo("Id", "Name", "IsEnabled", "KeyThumbprint");
        recipient.GetProperty("Id").GetString().Should().Be("ci-app");
        recipient.GetProperty("KeyThumbprint").GetString().Should().MatchRegex("^[0-9a-f]{16}$");
        view.RootElement.GetRawText().Should().NotContain(Modulus[..20]).And.NotContain("kek-").And.NotContain("Version");
    }

    [Fact]
    public void Issuer_CountsTheKeysOfItsJwks_NeverCopiesOne()
    {
        var jwks = "{\"keys\":[{\"kty\":\"RSA\",\"kid\":\"one\",\"n\":\"" + Modulus + "\",\"e\":\"AQAB\"},{\"kty\":\"RSA\",\"kid\":\"two\",\"n\":\"" + Modulus + "\",\"e\":\"AQAB\"}]}";
        var issuer = new FederatedIssuer { Name = "k8s", Issuer = "https://k8s.example.com", KeySource = FederatedIssuerKeySource.Jwks, Jwks = jwks, Description = "Cluster" };

        var value = AccessAuditValues.Issuer(issuer);

        value.Should().MatchRegex("^\\{\"Issuer\":\"https://k8s\\.example\\.com\",\"KeySource\":\"Jwks\",\"JwksKeys\":2,\"JwksThumbprint\":\"[0-9a-f]{16}\",\"Description\":\"Cluster\"\\}$");
        value.Should().NotContain(Modulus);
        AccessAuditValues.Issuer(issuer with { Jwks = "not json" }).Should().NotContain("JwksKeys").And.NotContain("not json");
    }

    [Fact]
    public void Issuer_ChangesWithItsKeys_ButNotWithTheirFormatting()
    {
        var one = "{\"keys\":[{\"kty\":\"RSA\",\"kid\":\"one\",\"n\":\"" + Modulus + "\",\"e\":\"AQAB\"}]}";
        var other = "{\"keys\":[{\"kty\":\"RSA\",\"kid\":\"other\",\"n\":\"" + Modulus + "x\",\"e\":\"AQAB\"}]}";
        var reformatted = "{ \"keys\": [ { \"kty\": \"RSA\", \"kid\": \"one\", \"n\": \"" + Modulus + "\", \"e\": \"AQAB\" } ] }";
        var issuer = new FederatedIssuer { Name = "k8s", Issuer = "https://k8s.example.com", KeySource = FederatedIssuerKeySource.Jwks, Jwks = one };

        // the same number of keys, but another key: the trust changed, so the view does
        AccessAuditValues.Issuer(issuer with { Jwks = other }).Should().NotBe(AccessAuditValues.Issuer(issuer));
        AccessAuditValues.Issuer(issuer with { Jwks = reformatted }).Should().Be(AccessAuditValues.Issuer(issuer));
    }

    [Fact]
    public void Certificate_IsItsLabelAndValidity_NeverThePfxOrItsPassword()
    {
        var certificate = new SharedCertificate
        {
            Thumbprint = "AB12",
            Label = "edge",
            NotBefore = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            NotAfter = new DateTimeOffset(2027, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Certificate = "MIIpfx-content",
            CertificatePassword = "pfx-password",
        };

        AccessAuditValues.Certificate(certificate).Should().Be("{\"Label\":\"edge\",\"NotBefore\":\"2026-01-01T00:00:00+00:00\",\"NotAfter\":\"2027-01-01T00:00:00+00:00\"}");
        AccessAuditValues.Certificate(certificate with { IsRevoked = true }).Should().EndWith(",\"IsRevoked\":true}");
    }

    [Fact]
    public void Records_TakeTheirTimeIdAndTtlFromTheClockAndTheRetention()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 15, 10, 20, 30, TimeSpan.Zero).AddTicks(1234567));
        var records = new AccessAuditRecords(Options.Create(new AccessAuditOptions { RetentionDays = 30 }), clock);
        var caller = new AccessCaller(AuditActor.Account("u-1", "Ada") with { Via = "mcp" }) { CorrelationId = "corr-1" };
        var change = new AccessAuditChange(AccessAuditAction.MemberGranted, caller) { Target = AccessAuditTarget.Account("u-2", "Bob"), NewValue = "Reader" };

        var ofProject = records.ForAccessPoint("acme.billing", change);
        var ofOrganization = records.ForAccessPoint("acme", change);
        var ofMachines = records.ForProject("acme", "billing", "billing.access", change with { Action = AccessAuditAction.MachineCreated });

        ofProject.Should().BeEquivalentTo(new
        {
            PartitionKey = "access",
            Organization = "acme",
            Project = "billing",
            AccessPointKey = "acme.billing",
            Time = "2026-11-15T10:20:30.1234567Z",
            Action = AccessAuditAction.MemberGranted,
            Actor = caller.Actor,
            Target = AccessAuditTarget.Account("u-2", "Bob"),
            OldValue = (string?)null,
            NewValue = "Reader",
            CorrelationId = "corr-1",
            TimeToLiveInSeconds = 30 * 24 * 60 * 60,
        });
        ofProject.Id.Should().MatchRegex("^aca\\.202611151020301234567\\.[0-9a-f]{32}$");
        ofOrganization.Project.Should().BeNull();
        ofOrganization.AccessPointKey.Should().Be("acme");
        ofOrganization.Id.Should().NotBe(ofProject.Id);
        ofMachines.PartitionKey.Should().Be("billing.access");
        ofMachines.Organization.Should().Be("acme");
        ofMachines.AccessPointKey.Should().Be("acme.billing");
        ofMachines.Action.Should().Be(AccessAuditAction.MachineCreated);
        ofProject.ToEntry().Time.Should().Be(clock.GetUtcNow());
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3651")]
    public void Options_ARetentionOutOfRange_FailsTheValidation(string retentionDays)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AccessAudit:RetentionDays"] = retentionDays })
            .Build();
        using var services = new ServiceCollection().AddCosmosDbAnnotations(configuration).BuildServiceProvider();

        var read = () => services.GetRequiredService<IOptions<AccessAuditOptions>>().Value;

        read.Should().Throw<OptionsValidationException>().Which.Message.Should().Contain("AccessAudit:RetentionDays");
    }

    // A project policy with every part that decides access: two levels besides Public, a customer key and a recipient.
    private static KeyValuePolicy Policy() => new()
    {
        AllowedProtections = [KeyValueProtection.Public, KeyValueProtection.Standard, KeyValueProtection.Hsm, KeyValueProtection.Byok],
        DefaultSecretProtection = KeyValueProtection.Standard,
        RequireMtlsForSecretDelivery = true,
        Hsm = new HsmSettings { Backend = HsmBackend.KeyVaultPremium, RotationInterval = TimeSpan.FromDays(180) },
        Byok = new ByokSettings { KeyUri = "https://customer.vault.azure.net/keys/kek", TenantId = "customer-tenant" },
        Recipients =
        [
            new ClientEncryptionRecipient
            {
                Id = "ci-app",
                Name = "CI",
                PublicKey = new RsaJsonWebKey { N = Modulus, E = "AQAB" },
                Created = AuditStamp.Of(AuditActor.Account("admin"), DateTimeOffset.UtcNow),
            },
        ],
        Version = 3,
    };

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
