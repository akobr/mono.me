#nullable enable

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.KeyValues.Audit;
using _42.Platform.Storyteller.KeyValues.ClientEncryption;
using _42.Platform.Storyteller.KeyValues.Policy;
using _42.Platform.Storyteller.KeyValues.Protection;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The project policy: allowed levels, their requirements, the customer key, recipients, and the audit of changes.
public class KeyValuePolicyTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Org = TestConstants.Organization;
    private const string View = Constants.DefaultViewName;

    private static readonly KeyValueCaller Admin = KeyValueCaller.WithSecrets(AuditActor.Account("admin"));

    private IKeyValuePolicyService Policies => Context.Services.GetRequiredService<IKeyValuePolicyService>();

    private IKeyValuePolicyStore Store => Context.Services.GetRequiredService<IKeyValuePolicyStore>();

    private IKeyValueService KeyValues => Context.Services.GetRequiredService<IKeyValueService>();

    private IKeyValueAuditLog Audit => Context.Services.GetRequiredService<IKeyValueAuditLog>();

    private FakeKeyEncryptionProvider StandardProvider => Context.Services.GetRequiredService<FakeKeyEncryptionProvider>();

    [Fact]
    public async Task Defaults_PublicAndStandard_WithMtlsRequired()
    {
        var policy = await Policies.GetAsync(Org, $"kv-nopolicy-{Guid.NewGuid():N}"[..30]);

        policy.AllowedProtections.Should().Equal(KeyValueProtection.Public, KeyValueProtection.Standard);
        policy.DefaultSecretProtection.Should().Be(KeyValueProtection.Standard);
        policy.RequireMtlsForSecretDelivery.Should().BeTrue();
        policy.Version.Should().Be(0);
        policy.Recipients.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_WithoutAnAccessPoint_IsNotFound()
    {
        var act = () => Policies.UpdateAsync(Org, $"kv-missing-{Guid.NewGuid():N}"[..30], new KeyValuePolicyUpdate { RequireMtlsForSecretDelivery = false }, Admin);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task EnableHsm_CreatesTheKeyAndRecordsTheBackend()
    {
        var project = await Context.CreateProjectWithAccessPointAsync("hsm");

        var policy = await Policies.UpdateAsync(Org, project, new KeyValuePolicyUpdate
        {
            AllowedProtections = [KeyValueProtection.Public, KeyValueProtection.Standard, KeyValueProtection.Hsm],
            DefaultSecretProtection = KeyValueProtection.Hsm,
        }, Admin);

        policy.Version.Should().Be(1);
        policy.Modified!.By.Should().Be(AuditActor.Account("admin"));
        policy.Hsm.Should().NotBeNull();
        policy.Hsm!.Backend.Should().Be(HsmBackend.KeyVaultPremium);
        policy.Hsm.Key!.KeyId.Should().Contain("kek-hsm");
        policy.DefaultSecretProtection.Should().Be(KeyValueProtection.Hsm);

        var written = await KeyValues.SetAsync(Org, project, View, "hsm.secret", new KeyValueWrite { Protection = KeyValueProtection.Hsm, Value = "s" }, Admin);
        written.Protection.Should().Be(KeyValueProtection.Hsm);
        (await KeyValues.GetAsync(Org, project, View, "hsm.secret", new KeyValueReadOptions { Reveal = true }, Admin))!.Value.Should().Be("s");

        var disable = () => Policies.UpdateAsync(Org, project, new KeyValuePolicyUpdate { AllowedProtections = [KeyValueProtection.Public, KeyValueProtection.Standard] }, Admin);
        (await disable.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.ProtectionInUse);

        await KeyValues.DeleteAsync(Org, project, View, "hsm.secret", new KeyValueDeleteOptions { Permanent = true }, Admin);
        var disabled = await Policies.UpdateAsync(Org, project, new KeyValuePolicyUpdate { AllowedProtections = [KeyValueProtection.Public, KeyValueProtection.Standard] }, Admin);
        disabled.AllowedProtections.Should().NotContain(KeyValueProtection.Hsm);
        disabled.DefaultSecretProtection.Should().Be(KeyValueProtection.Hsm, "the default is left alone until it is changed");

        var events = await Audit.QueryAsync(Org, project, new KeyValueAuditQuery { Operation = KeyValueAuditOperation.PolicyChange });
        events.Items.Should().HaveCount(2);
        events.Items[0].Detail.Should().Contain("-Hsm");
        events.Items[1].Detail.Should().Contain("+Hsm");
    }

    [Fact]
    public async Task EnableByok_NeedsAVerifiedCustomerKey()
    {
        var project = await Context.CreateProjectWithAccessPointAsync("byok");

        var tooEarly = () => Policies.UpdateAsync(Org, project, new KeyValuePolicyUpdate { AllowedProtections = [KeyValueProtection.Public, KeyValueProtection.Standard, KeyValueProtection.Byok] }, Admin);
        (await tooEarly.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.ByokNotConfigured);

        var badUri = () => Policies.UpdateAsync(Org, project, new KeyValuePolicyUpdate { Byok = new ByokSettingsUpdate { KeyUri = "not a uri", TenantId = Guid.NewGuid().ToString() } }, Admin);
        (await badUri.Should().ThrowAsync<InvalidInputException>()).Which.ErrorCode.Should().Be(ErrorCodes.CustomerKeyInvalid);

        var setup = Policies.GetByokSetup(Org, project);
        setup.ClientId.Should().Be("fake-client");

        var withKey = await Policies.UpdateAsync(Org, project, new KeyValuePolicyUpdate
        {
            Byok = new ByokSettingsUpdate { KeyUri = "https://customer.vault.azure.net/keys/bad-kek/abc", TenantId = Guid.NewGuid().ToString() },
        }, Admin);
        withKey.Byok!.KeyUri.Should().Be("https://customer.vault.azure.net/keys/bad-kek", "the version is dropped");
        withKey.Byok.Health.Should().Be(CustomerKeyHealth.Unknown);

        var failedVerification = () => Policies.VerifyByokAsync(Org, project, Admin);
        (await failedVerification.Should().ThrowAsync<FailedDependencyException>()).Which.ErrorCode.Should().Be(ErrorCodes.CustomerKeyInvalid);
        (await Store.GetAsync(Org, project)).Byok!.Health.Should().Be(CustomerKeyHealth.Unavailable);

        await Policies.UpdateAsync(Org, project, new KeyValuePolicyUpdate
        {
            Byok = new ByokSettingsUpdate { KeyUri = "https://customer.vault.azure.net/keys/good-kek", TenantId = Guid.NewGuid().ToString() },
        }, Admin);
        var verified = await Policies.VerifyByokAsync(Org, project, Admin);
        verified.Byok!.VerifiedAt.Should().NotBeNull();
        verified.Byok.Health.Should().Be(CustomerKeyHealth.Healthy);
        verified.Byok.CurrentKeyVersion.Should().Be("v1");

        var enabled = await Policies.UpdateAsync(Org, project, new KeyValuePolicyUpdate { AllowedProtections = [KeyValueProtection.Public, KeyValueProtection.Standard, KeyValueProtection.Byok] }, Admin);
        enabled.AllowedProtections.Should().Contain(KeyValueProtection.Byok);

        var written = await KeyValues.SetAsync(Org, project, View, "customer.secret", new KeyValueWrite { Protection = KeyValueProtection.Byok, Value = "c" }, Admin);
        written.Protection.Should().Be(KeyValueProtection.Byok);

        var removeInUse = () => Policies.UpdateAsync(Org, project, new KeyValuePolicyUpdate { Byok = new ByokSettingsUpdate { KeyUri = string.Empty } }, Admin);
        (await removeInUse.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.ProtectionInUse);

        // a revoked key makes the project's BYOK reads fail with 424, and the daily check records it
        var customer = Context.Services.GetRequiredService<FakeCustomerKeyProvider>();
        customer.IsRevoked = true;

        try
        {
            var read = () => KeyValues.GetAsync(Org, project, View, "customer.secret", new KeyValueReadOptions { Reveal = true }, Admin);
            (await read.Should().ThrowAsync<FailedDependencyException>()).Which.ErrorCode.Should().Be(ErrorCodes.CustomerKeyUnavailable);

            var checkedPolicy = await Context.Services.GetRequiredService<IKeyValueMaintenanceService>().CheckKeysAsync(Org, project, force: true);
            checkedPolicy.Byok!.Health.Should().Be(CustomerKeyHealth.Unavailable);
            checkedPolicy.Byok.LastError.Should().Contain("disabled");

            var events = await Audit.QueryAsync(Org, project, new KeyValueAuditQuery { Operation = KeyValueAuditOperation.ByokUnavailable });
            events.Items.Should().ContainSingle();
        }
        finally
        {
            customer.IsRevoked = false;
        }

        var healthy = await Context.Services.GetRequiredService<IKeyValueMaintenanceService>().CheckKeysAsync(Org, project, force: true);
        healthy.Byok!.Health.Should().Be(CustomerKeyHealth.Healthy);
    }

    [Fact]
    public async Task Recipients_AndHyok()
    {
        var project = await Context.CreateProjectWithAccessPointAsync("hyok");
        using var rsa = RSA.Create(3072);
        using var small = RSA.Create(2048);

        var tooSmall = () => Policies.AddRecipientAsync(Org, project, new ClientEncryptionRecipientCreate { Name = "small", PublicKey = small.ExportSubjectPublicKeyInfoPem() }, Admin);
        (await tooSmall.Should().ThrowAsync<InvalidInputException>()).Which.ErrorCode.Should().Be(ErrorCodes.RecipientInvalid);

        var noRecipients = () => Policies.UpdateAsync(Org, project, new KeyValuePolicyUpdate { AllowedProtections = [KeyValueProtection.Public, KeyValueProtection.Standard, KeyValueProtection.Hyok] }, Admin);
        (await noRecipients.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.RecipientInvalid);

        var recipient = await Policies.AddRecipientAsync(Org, project, new ClientEncryptionRecipientCreate { Id = "billing-app", Name = "Billing", PublicKey = rsa.ExportSubjectPublicKeyInfoPem() }, Admin);
        recipient.PublicKey.Kty.Should().Be("RSA");
        recipient.IsEnabled.Should().BeTrue();

        var duplicate = () => Policies.AddRecipientAsync(Org, project, new ClientEncryptionRecipientCreate { Id = "billing-app", Name = "Again", PublicKey = rsa.ExportSubjectPublicKeyInfoPem() }, Admin);
        (await duplicate.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.RecipientExists);

        var jwkRecipient = await Policies.AddRecipientAsync(Org, project, new ClientEncryptionRecipientCreate { Name = "By JWK", PublicKey = RsaPublicJwk.FromRsa(rsa).ToJson() }, Admin);
        jwkRecipient.Id.Should().HaveLength(16);

        await Policies.UpdateAsync(Org, project, new KeyValuePolicyUpdate { AllowedProtections = [KeyValueProtection.Public, KeyValueProtection.Standard, KeyValueProtection.Hyok] }, Admin);

        var place = $"{Org}/{project}/{View}/partner.key";
        var recipients = new[] { new ClientEncryptionRecipientKey("billing-app", RsaPublicJwk.FromRsa(rsa)) };
        var document = ClientSecretEncryptor.Encrypt("top secret", recipients, place);

        var written = await KeyValues.SetAsync(Org, project, View, "partner.key", new KeyValueWrite { Protection = KeyValueProtection.Hyok, Value = document }, Admin);
        written.Protection.Should().Be(KeyValueProtection.Hyok);
        written.Recipients.Should().Equal("billing-app");

        var revealed = await KeyValues.GetAsync(Org, project, View, "partner.key", new KeyValueReadOptions { Reveal = true }, Admin);
        revealed!.Value.Should().Be(document, "the platform returns the document as it is");
        ClientSecretDecryptor.Decrypt(revealed.Value!, "billing-app", rsa).Should().Be("top secret");

        var wrongPlace = () => KeyValues.SetAsync(Org, project, View, "other.key", new KeyValueWrite { Protection = KeyValueProtection.Hyok, Value = document }, Admin);
        (await wrongPlace.Should().ThrowAsync<InvalidInputException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvalidClientEncryptedValue);

        var unknown = ClientSecretEncryptor.Encrypt("x", [new ClientEncryptionRecipientKey("nobody", RsaPublicJwk.FromRsa(rsa))], place);
        var unknownRecipient = () => KeyValues.SetAsync(Org, project, View, "partner.key", new KeyValueWrite { Value = unknown }, Admin);
        (await unknownRecipient.Should().ThrowAsync<InvalidInputException>()).Which.ErrorCode.Should().Be(ErrorCodes.UnknownRecipient);

        var diff = () => KeyValues.GetVersionChangesAsync(Org, project, View, "partner.key", 0, 1, Admin);
        (await diff.Should().ThrowAsync<InvalidInputException>()).Which.ErrorCode.Should().Be(ErrorCodes.NotSupportedForProtection);

        var protect = await KeyValues.ProtectAsync(Org, project, View, new KeyValueBulkRequest { Keys = ["partner.key"], Protection = KeyValueProtection.Standard }, Admin);
        protect.Items.Single().Outcome.Should().Be(KeyValueBulkOutcome.NotAllowed);

        var disableLast = () => Policies.UpdateRecipientAsync(Org, project, "billing-app", new ClientEncryptionRecipientUpdate { IsEnabled = false }, Admin);
        await disableLast.Should().NotThrowAsync("another enabled recipient remains");
        var removeLast = () => Policies.RemoveRecipientAsync(Org, project, jwkRecipient.Id, Admin);
        (await removeLast.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.ProtectionInUse);

        var missing = () => Policies.RemoveRecipientAsync(Org, project, "nope", Admin);
        (await missing.Should().ThrowAsync<NotFoundException>()).Which.ErrorCode.Should().Be(ErrorCodes.RecipientNotFound);
    }

    [Fact]
    public async Task Update_ChecksTheExpectedVersionAndTheDefaultLevel()
    {
        var project = await Context.CreateProjectWithAccessPointAsync("policy");

        var first = await Policies.UpdateAsync(Org, project, new KeyValuePolicyUpdate { RequireMtlsForSecretDelivery = false }, Admin);
        first.Version.Should().Be(1);
        first.RequireMtlsForSecretDelivery.Should().BeFalse();

        var stale = () => Policies.UpdateAsync(Org, project, new KeyValuePolicyUpdate { RequireMtlsForSecretDelivery = true, ExpectedVersion = 0 }, Admin);
        (await stale.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.PolicyVersionMismatch);

        var publicDefault = () => Policies.UpdateAsync(Org, project, new KeyValuePolicyUpdate { DefaultSecretProtection = KeyValueProtection.Public }, Admin);
        await publicDefault.Should().ThrowAsync<InvalidInputException>();
        var notAllowedDefault = () => Policies.UpdateAsync(Org, project, new KeyValuePolicyUpdate { DefaultSecretProtection = KeyValueProtection.Hsm }, Admin);
        await notAllowedDefault.Should().ThrowAsync<InvalidInputException>();
        var noPublic = () => Policies.UpdateAsync(Org, project, new KeyValuePolicyUpdate { AllowedProtections = [KeyValueProtection.Standard] }, Admin);
        await noPublic.Should().ThrowAsync<InvalidInputException>();

        var unchanged = await Policies.UpdateAsync(Org, project, new KeyValuePolicyUpdate { RequireMtlsForSecretDelivery = false, ExpectedVersion = 1 }, Admin);
        unchanged.Version.Should().Be(1, "nothing changed, nothing is written");
    }

    // 424-167: the first Standard secret records the project's Standard key, so Settings shows it with Rotate now.
    [Fact]
    public async Task FirstStandardSecret_RecordsTheStandardKey()
    {
        var project = await Context.CreateProjectWithAccessPointAsync("first");
        var before = await Policies.GetAsync(Org, project);
        before.Standard.Should().BeNull();
        before.Version.Should().Be(0);

        await KeyValues.SetAsync(Org, project, View, "db.password", new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "s" }, Admin);

        var policy = await Policies.GetAsync(Org, project);
        policy.Standard.Should().NotBeNull();
        policy.Standard!.KeyId.Should().Be(StandardProvider.KeyId);
        policy.Standard.CurrentKeyVersion.Should().Be(StandardProvider.CurrentVersion);
        policy.Standard.CheckedAt.Should().NotBeNull();
        policy.Version.Should().Be(1);
        policy.Modified!.By.Should().Be(AuditActor.System);
        policy.AllowedProtections.Should().Equal(KeyValueProtection.Public, KeyValueProtection.Standard);

        // stored, not only cached: the maintenance lists the project from now on
        var stored = (await Store.ListAsync()).Where(entry => entry.Organization == Org && entry.Project == project).ToList();
        stored.Should().ContainSingle();
        stored[0].Policy.Standard!.KeyId.Should().Be(StandardProvider.KeyId);

        await KeyValues.SetAsync(Org, project, View, "api.key", new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "k" }, Admin);
        await KeyValues.SetAsync(Org, project, View, "db.password", new KeyValueWrite { Value = "s2" }, Admin);
        (await Policies.GetAsync(Org, project)).Version.Should().Be(1, "a known key is not recorded again");

        var changes = await Audit.QueryAsync(Org, project, new KeyValueAuditQuery { Operation = KeyValueAuditOperation.PolicyChange });
        changes.Items.Should().BeEmpty("the platform's record of its own key is not a policy change");
    }

    [Fact]
    public async Task PublicPair_RecordsNoKey()
    {
        var project = await Context.CreateProjectWithAccessPointAsync("public");

        await KeyValues.SetAsync(Org, project, View, "db.host", new KeyValueWrite { Protection = KeyValueProtection.Public, Value = "localhost" }, Admin);

        var policy = await Policies.GetAsync(Org, project);
        policy.Standard.Should().BeNull();
        policy.Version.Should().Be(0);
        (await Store.ListAsync()).Should().NotContain(entry => entry.Project == project);
    }

    [Fact]
    public async Task StandardUpdate_RecordsTheKey()
    {
        var project = await Context.CreateProjectWithAccessPointAsync("update");
        await KeyValues.SetAsync(Org, project, View, "token", new KeyValueWrite { Protection = KeyValueProtection.Public, Value = "t1" }, Admin);
        (await Policies.GetAsync(Org, project)).Standard.Should().BeNull();

        await KeyValues.SetAsync(Org, project, View, "token", new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "t2" }, Admin);

        var policy = await Policies.GetAsync(Org, project);
        policy.Standard!.KeyId.Should().Be(StandardProvider.KeyId);
        policy.Version.Should().Be(1);
    }

    [Fact]
    public async Task Import_OfAStandardSecret_RecordsTheKeyOnce()
    {
        var project = await Context.CreateProjectWithAccessPointAsync("import");
        var document = new KeyValueDocument
        {
            Items =
            [
                new KeyValueDocumentItem { Key = "db.host", Protection = KeyValueProtection.Public, Value = "localhost" },
                new KeyValueDocumentItem { Key = "db.password", Protection = KeyValueProtection.Standard, Value = "s" },
                new KeyValueDocumentItem { Key = "api.key", Protection = KeyValueProtection.Standard, Value = "k" },
            ],
        };

        var dryRun = await KeyValues.ImportAsync(Org, project, View, document, new KeyValueImportOptions { DryRun = true }, Admin);
        dryRun.IsDryRun.Should().BeTrue();
        (await Policies.GetAsync(Org, project)).Standard.Should().BeNull("a dry run writes nothing");

        var result = await KeyValues.ImportAsync(Org, project, View, document, new KeyValueImportOptions(), Admin);

        result.IsComplete.Should().BeTrue();
        var policy = await Policies.GetAsync(Org, project);
        policy.Standard!.KeyId.Should().Be(StandardProvider.KeyId);
        policy.Version.Should().Be(1);
    }

    [Fact]
    public async Task Protect_ToStandard_RecordsTheKey()
    {
        var project = await Context.CreateProjectWithAccessPointAsync("protect");
        var allowHsm = new KeyValuePolicyUpdate { AllowedProtections = [KeyValueProtection.Public, KeyValueProtection.Standard, KeyValueProtection.Hsm] };
        var enabled = await Policies.UpdateAsync(Org, project, allowHsm, Admin);
        enabled.Hsm!.Key.Should().NotBeNull("enabling the level records the HSM key");
        enabled.Standard.Should().BeNull();
        await KeyValues.SetAsync(Org, project, View, "s", new KeyValueWrite { Protection = KeyValueProtection.Hsm, Value = "v" }, Admin);
        (await Policies.GetAsync(Org, project)).Standard.Should().BeNull("an HSM secret records no Standard key");

        var result = await KeyValues.ProtectAsync(Org, project, View, new KeyValueBulkRequest { Keys = ["s"], Protection = KeyValueProtection.Standard }, Admin);

        result.Items.Single().Outcome.Should().Be(KeyValueBulkOutcome.Done);
        var policy = await Policies.GetAsync(Org, project);
        policy.Standard!.KeyId.Should().Be(StandardProvider.KeyId);
        policy.Hsm!.Key.Should().BeEquivalentTo(enabled.Hsm.Key, "the record changes nothing else");
        policy.Version.Should().Be(2);
    }

    [Fact]
    public async Task ConcurrentFirstStandardSecrets_RecordTheKeyOnce()
    {
        var project = await Context.CreateProjectWithAccessPointAsync("burst");

        var written = await Task.WhenAll(Enumerable.Range(0, 8).Select(index =>
            KeyValues.SetAsync(Org, project, View, $"secret.k{index}", new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = $"v{index}" }, Admin)));

        written.Should().OnlyContain(pair => pair.Protection == KeyValueProtection.Standard);
        var policy = await Policies.GetAsync(Org, project);
        policy.Standard!.KeyId.Should().Be(StandardProvider.KeyId);
        policy.Version.Should().Be(1, "the writers that lost the race read the recorded key and stop");
    }

    // Two API instances share the database, each with its own cache. After one records the key with the first Standard
    // secret, the other shows the recorded policy and takes an Administrator's change at its version, although its cache
    // still holds the defaults.
    [Fact]
    public async Task AnotherInstance_ShowsAndAcceptsTheRecordedPolicy()
    {
        var project = await Context.CreateProjectWithAccessPointAsync("instances");
        using var otherCache = new MemoryCache(new MemoryCacheOptions());
        var repositories = Context.Services.GetRequiredService<IContainerRepositoryProvider>();
        var otherStore = new CosmosKeyValuePolicyStore(repositories, Context.Services.GetRequiredService<IOptions<JsonSerializerOptions>>(), otherCache);
        var otherPolicies = new CosmosKeyValuePolicyService(
            otherStore,
            Context.Services.GetServices<IKeyEncryptionProvider>(),
            repositories,
            Context.Services.GetRequiredService<IOptions<KeyValueOptions>>());
        (await otherStore.GetAsync(Org, project)).Version.Should().Be(0, "the other instance caches the defaults");

        await KeyValues.SetAsync(Org, project, View, "db.password", new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "s" }, Admin);
        (await otherStore.GetAsync(Org, project)).Standard.Should().BeNull("the other instance's cache is stale now");

        var updated = await otherPolicies.UpdateAsync(Org, project, new KeyValuePolicyUpdate { RequireMtlsForSecretDelivery = false, ExpectedVersion = 1 }, Admin);

        updated.Version.Should().Be(2);
        updated.RequireMtlsForSecretDelivery.Should().BeFalse();
        updated.Standard!.KeyId.Should().Be(StandardProvider.KeyId, "the change keeps the recorded key");

        var shown = await Policies.GetAsync(Org, project);
        shown.Version.Should().Be(2, "the policy endpoint reads past this instance's cache, which still holds version 1");
        shown.Standard!.KeyId.Should().Be(StandardProvider.KeyId);
    }

    [Fact]
    public async Task StandardSecret_WithoutAnAccessPoint_IsStoredWithoutAPolicy()
    {
        var project = $"kv-noapt-{Guid.NewGuid():N}"[..30];

        var written = await KeyValues.SetAsync(Org, project, View, "s", new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "v" }, Admin);

        written.Version.Should().Be(1);
        (await KeyValues.GetAsync(Org, project, View, "s", new KeyValueReadOptions { Reveal = true }, Admin))!.Value.Should().Be("v");
        var policy = await Policies.GetAsync(Org, project);
        policy.Standard.Should().BeNull();
        policy.Version.Should().Be(0);
        (await Store.ListAsync()).Should().NotContain(entry => entry.Project == project);
    }
}
