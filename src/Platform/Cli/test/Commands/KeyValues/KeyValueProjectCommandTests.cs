using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Cli.Commands.KeyValues;
using _42.Platform.Storyteller.KeyValues.ClientEncryption;
using _42.Platform.Storyteller.Sdk;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands.KeyValues;

// The project-scoped commands: policy, recipients, audit, usage, and the re-encryption of hyok values.
public class KeyValueProjectCommandTests
{
    private readonly RecordingConsole _console = new();
    private readonly Mock<IKeyValuesApiClient> _api = new();

    [Fact]
    public async Task PolicySet_SendsOnlyTheGivenMembers()
    {
        KeyValuePolicyUpdate? sent = null;
        _api.Setup(api => api.SetKeyValuePolicyAsync("org1", "proj1", It.IsAny<KeyValuePolicyUpdate>()))
            .Callback<string, string, KeyValuePolicyUpdate>((_, _, update) => sent = update)
            .ReturnsAsync(new KeyValuePolicy { Version = 2 });

        var exitCode = await new KeyValuePolicySetCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { Allow = ["public", "standard", "HSM"], RequireMtls = "false", HsmRotationDays = 90 }.OnExecuteAsync();
        var nothing = await new KeyValuePolicySetCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object).OnExecuteAsync();
        var badBool = await new KeyValuePolicySetCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { RequireMtls = "maybe" }.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        sent!.AllowedProtections.ShouldBe([AllowedProtections2.Public, AllowedProtections2.Standard, AllowedProtections2.Hsm]);
        sent.DefaultSecretProtection.ShouldBeNull("not given, so kept");
        sent.RequireMtlsForSecretDelivery.ShouldBe(false);
        sent.HsmRotationInterval.ShouldBe("90.00:00:00");
        sent.Byok.ShouldBeNull();
        nothing.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        badBool.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.Lines.ShouldContain(line => line.Contains("is at version 2"));
    }

    [Fact]
    public async Task PolicySet_ByokSettings_AndRotateOnlyStandardOrHsm()
    {
        KeyValuePolicyUpdate? sent = null;
        _api.Setup(api => api.SetKeyValuePolicyAsync("org1", "proj1", It.IsAny<KeyValuePolicyUpdate>()))
            .Callback<string, string, KeyValuePolicyUpdate>((_, _, update) => sent = update)
            .ReturnsAsync(new KeyValuePolicy());
        _api.Setup(api => api.RotateKeyValueKeyAsync("org1", "proj1", It.IsAny<KeyValueRotateRequest>()))
            .ReturnsAsync(new KeyValuePolicy { Standard = new ProtectionKeyInfo { KeyId = "k", CurrentKeyVersion = "v2" }, Rewrap = new RewrapStatus { Protection = RewrapStatusProtection.Standard, TargetKeyVersion = "v2", StartedAt = DateTimeOffset.UnixEpoch, Processed = 0, Remaining = 12 } });

        await new KeyValuePolicySetCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { ByokKey = "https://customer.vault.azure.net/keys/kek", ByokTenant = "tenant" }.OnExecuteAsync();
        var rotated = await new KeyValueRotateCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { Protection = "standard" }.OnExecuteAsync();
        var byok = await new KeyValueRotateCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { Protection = "byok" }.OnExecuteAsync();

        sent!.Byok!.KeyUri.ShouldBe("https://customer.vault.azure.net/keys/kek");
        sent.Byok.TenantId.ShouldBe("tenant");
        rotated.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldContain(line => line.Contains("version v2"));
        _console.Lines.ShouldContain(line => line.Contains("12 remaining"));
        byok.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _api.Verify(api => api.RotateKeyValueKeyAsync("org1", "proj1", It.Is<KeyValueRotateRequest>(request => request.Protection == KeyValueRotateRequestProtection.Standard)), Times.Once);
    }

    [Fact]
    public async Task ByokSetupAndVerify_ExplainTheSteps()
    {
        _api.Setup(api => api.GetByokSetupAsync("org1", "proj1")).ReturnsAsync(new ByokSetupInfo { ClientId = "app-id", ConsentUrl = "https://login.microsoftonline.com/organizations/adminconsent?client_id=app-id", RequiredRole = "Key Vault Crypto Service Encryption User" });
        _api.Setup(api => api.VerifyByokAsync("org1", "proj1")).ReturnsAsync(new KeyValuePolicy { Byok = new ByokSettings { KeyUri = "https://c.vault.azure.net/keys/kek", TenantId = "t", Health = ByokSettingsHealth.Healthy, CurrentKeyVersion = "abc", VerifiedAt = DateTimeOffset.UnixEpoch } });

        var setup = await new KeyValueByokSetupCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object).OnExecuteAsync();
        var verify = await new KeyValueByokVerifyCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object).OnExecuteAsync();

        setup.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldContain(line => line.Contains("adminconsent?client_id=app-id"));
        _console.Lines.ShouldContain(line => line.Contains("Key Vault Crypto Service Encryption User"));
        verify.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldContain(line => line.Contains("is Healthy") && line.Contains("version abc"));
    }

    [Fact]
    public async Task Recipients_AddFromAFile_EnableDisable_RemoveAsks()
    {
        ClientEncryptionRecipientCreate? created = null;
        _api.Setup(api => api.AddRecipientAsync("org1", "proj1", It.IsAny<ClientEncryptionRecipientCreate>()))
            .Callback<string, string, ClientEncryptionRecipientCreate>((_, _, create) => created = create)
            .ReturnsAsync(new ClientEncryptionRecipient { Id = "api", Name = "API", IsEnabled = true });
        _api.Setup(api => api.UpdateRecipientAsync("org1", "proj1", "api", It.IsAny<ClientEncryptionRecipientUpdate>()))
            .ReturnsAsync((string _, string _, string id, ClientEncryptionRecipientUpdate update) => new ClientEncryptionRecipient { Id = id, Name = id, IsEnabled = update.IsEnabled ?? true });
        var files = KeyValueTestContext.Files(("api.pem", "-----BEGIN PUBLIC KEY-----\nMIIB\n-----END PUBLIC KEY-----"));

        var added = await new KeyValueRecipientAddCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, files) { Name = "API", PublicKey = "api.pem" }.OnExecuteAsync();
        var incomplete = await new KeyValueRecipientAddCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, files) { Name = "API" }.OnExecuteAsync();
        var disabled = await new KeyValueRecipientDisableCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { Id = "api" }.OnExecuteAsync();
        _console.Confirm(false);
        var kept = await new KeyValueRecipientRemoveCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { Id = "api" }.OnExecuteAsync();
        var removed = await new KeyValueRecipientRemoveCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { Id = "api", Yes = true }.OnExecuteAsync();

        added.ShouldBe(ExitCodes.SUCCESS);
        created!.PublicKey.ShouldStartWith("-----BEGIN PUBLIC KEY-----");
        created.Name.ShouldBe("API");
        _console.Lines.ShouldContain(line => line.Contains("reencrypt"));
        incomplete.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        disabled.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldContain(line => line.Contains("'api' is disabled"));
        kept.ShouldBe(ExitCodes.WARNING_ABORTED);
        removed.ShouldBe(ExitCodes.SUCCESS);
        _api.Verify(api => api.RemoveRecipientAsync("org1", "proj1", "api"), Times.Once);
    }

    [Fact]
    public async Task Reencrypt_DecryptsWithTheOldKey_AndWritesForTheCurrentRecipients()
    {
        using var old = new TestRecipient("old");
        using var api = new TestRecipient("api");
        var oldDocument = ClientSecretEncryptor.Encrypt("plain", [new ClientEncryptionRecipientKey("old", RsaPublicJwk.FromRsa(old.Rsa))], "org1/proj1/default/db.password");
        var writes = new List<(string Key, KeyValueWrite Write)>();
        _api.Setup(a => a.GetRecipientsAsync("org1", "proj1")).ReturnsAsync([api.Model]);
        _api.Setup(a => a.GetKeyValuesAsync("org1", "proj1", "default", "db.", "Hyok", null, null, null))
            .ReturnsAsync(new KeyValuesResponse { Items = [Summary("db.password", KeyValueSummaryProtection.Hyok), Summary("db.other", KeyValueSummaryProtection.Hyok)] });
        _api.Setup(a => a.GetKeyValueAsync("org1", "proj1", "default", "db.password", true, null)).ReturnsAsync(KeyValueTestContext.Pair("db.password", KeyValueProtection.Hyok, oldDocument, 4));
        _api.Setup(a => a.GetKeyValueAsync("org1", "proj1", "default", "db.other", true, null))
            .ReturnsAsync(KeyValueTestContext.Pair("db.other", KeyValueProtection.Hyok, ClientSecretEncryptor.Encrypt("x", [new ClientEncryptionRecipientKey("api", RsaPublicJwk.FromRsa(api.Rsa))], "org1/proj1/default/db.other")));
        _api.Setup(a => a.SetKeyValueAsync("org1", "proj1", "default", It.IsAny<string>(), It.IsAny<KeyValueWrite>()))
            .Callback<string, string, string, string, KeyValueWrite>((_, _, _, key, write) => writes.Add((key, write)))
            .ReturnsAsync((string _, string _, string _, string key, KeyValueWrite _) => KeyValueTestContext.Pair(key, KeyValueProtection.Hyok, null, 5));

        var exitCode = await new KeyValueReencryptCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, Mock.Of<IFileSystem>()) { Prefix = "db.", PrivateKeyPath = old.PrivateKeyPem, RecipientId = "old" }.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_PARTIAL_RESULT, "db.other was not encrypted for 'old'");
        writes.Select(write => write.Key).ShouldBe(["db.password"]);
        writes[0].Write.Protection.ShouldBe(KeyValueWriteProtection.Hyok);
        writes[0].Write.ExpectedVersion.ShouldBe(4);
        api.Decrypt(writes[0].Write.Value).ShouldBe("plain");
        JweDocument.Parse(writes[0].Write.Value).Recipients.Select(recipient => recipient.KeyId).ShouldBe(["api"]);
        _console.Lines.ShouldContain(line => line.Contains("1 value(s) encrypted again"));
        _console.Lines.ShouldContain(line => line.Contains("skipped db.other"));
    }

    [Fact]
    public async Task Audit_PassesTheFilters_AndRejectsUnknownOperations()
    {
        _api.Setup(api => api.GetKeyValueAuditAsync("org1", "proj1", null, "db.password", "Reveal", It.IsAny<DateTimeOffset?>(), null, null))
            .ReturnsAsync(new KeyValueAuditResponse { Items = [new KeyValueAuditEvent { Id = "1", Time = DateTimeOffset.UnixEpoch, Operation = KeyValueAuditEventOperation.Reveal, Actor = "account: ada", Channel = KeyValueAuditEventChannel.User, View = "default", Key = "db.password", Count = 1 }], Count = 1 });

        var exitCode = await new KeyValueAuditCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { Key = "db.password", Operation = "reveal", From = "2026-10-01" }.OnExecuteAsync();
        var unknown = await new KeyValueAuditCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { Operation = "peek" }.OnExecuteAsync();
        var badTime = await new KeyValueAuditCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { From = "yesterday" }.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        unknown.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        badTime.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _api.Verify(api => api.GetKeyValueAuditAsync("org1", "proj1", null, "db.password", "Reveal", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), null, null), Times.Once);
    }

    [Fact]
    public async Task Usage_PassesTheDays_AndWritesTheTotals()
    {
        _api.Setup(api => api.GetKeyValueUsageAsync("org1", "proj1", "2026-09-01", "2026-09-30"))
            .ReturnsAsync(new KeyValueUsageReport
            {
                From = new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
                To = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero),
                Totals = new Dictionary<string, double> { ["keys.standard"] = 12.5, ["reads.api"] = 300 },
                EstimatedCharges = new Dictionary<string, double> { ["keys.standard"] = 1.25 },
                Days = [],
            });

        var exitCode = await new KeyValueUsageCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { From = "2026-09-01", To = "2026-09-30" }.OnExecuteAsync();
        var badDay = await new KeyValueUsageCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { From = "September" }.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldContain(line => line.Contains("2026-09-01 to 2026-09-30"));
        badDay.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
    }

    private static KeyValueSummary Summary(string key, KeyValueSummaryProtection protection) => new()
    {
        Key = key,
        Protection = protection,
        Version = 1,
        Author = "a",
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };
}
