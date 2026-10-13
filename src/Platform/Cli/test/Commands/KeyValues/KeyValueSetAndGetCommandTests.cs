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

public class KeyValueSetAndGetCommandTests
{
    private readonly RecordingConsole _console = new();
    private readonly Mock<IKeyValuesApiClient> _api = new();
    private KeyValueWrite? _written;

    public KeyValueSetAndGetCommandTests()
    {
        _api.Setup(api => api.GetKeyValuePolicyAsync("org1", "proj1"))
            .ReturnsAsync(new KeyValuePolicy { DefaultSecretProtection = KeyValuePolicyDefaultSecretProtection.Standard });
        _api.Setup(api => api.SetKeyValueAsync("org1", "proj1", "default", It.IsAny<string>(), It.IsAny<KeyValueWrite>()))
            .Callback<string, string, string, string, KeyValueWrite>((_, _, _, _, write) => _written = write)
            .ReturnsAsync((string _, string _, string _, string key, KeyValueWrite write) =>
                KeyValueTestContext.Pair(key, write.Protection is null ? KeyValueProtection.Standard : System.Enum.Parse<KeyValueProtection>(write.Protection.ToString()!), null, 2));
    }

    [Fact]
    public async Task Set_SecretWithoutAValue_AsksWithHiddenInput()
    {
        _console.Password("s3cret");

        var exitCode = await Set(new KeyValueSetCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, Mock.Of<IFileSystem>()) { Key = "db.password", Protection = "standard" });

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _written!.Protection.ShouldBe(KeyValueWriteProtection.Standard);
        _written.Value.ShouldBe("s3cret");
        _console.Lines.ShouldContain(line => line.Contains("'db.password' is at version 2"));
    }

    [Fact]
    public async Task Set_SecretAsAnArgument_Warns()
    {
        await Set(new KeyValueSetCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, Mock.Of<IFileSystem>()) { Key = "db.password", Value = "s", Secret = true });

        _console.Lines.ShouldContain(line => line.Contains("shell history"));
        _written!.Protection.ShouldBe(KeyValueWriteProtection.Standard, "the project default secret level");
    }

    [Fact]
    public async Task Set_NewKeyWithoutALevel_IsPublic_AndAnExistingOneKeepsIts()
    {
        _api.Setup(api => api.GetKeyValueAsync("org1", "proj1", "default", "db.host", null, null)).ThrowsAsync(KeyValueTestContext.NotFound("db.host"));
        _api.Setup(api => api.GetKeyValueAsync("org1", "proj1", "default", "db.password", null, null)).ReturnsAsync(KeyValueTestContext.Pair("db.password", KeyValueProtection.Hsm, null));

        await Set(new KeyValueSetCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, Mock.Of<IFileSystem>()) { Key = "db.host", Value = "sql" });
        _written!.Protection.ShouldBe(KeyValueWriteProtection.Public);
        _console.Lines.ShouldContain(line => line.Contains("New key, stored as Public"));

        await Set(new KeyValueSetCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, Mock.Of<IFileSystem>()) { Key = "db.password", Value = "new" });
        _written!.Protection.ShouldBeNull("an update without a level keeps the level");
    }

    [Fact]
    public async Task Set_Reference_IsAPublicPairWithTheReferenceContentType()
    {
        await Set(new KeyValueSetCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, Mock.Of<IFileSystem>()) { Key = "db.password", Value = "kv://vault/db-password", Reference = true });

        _written!.Protection.ShouldBe(KeyValueWriteProtection.Public);
        _written.ContentType.ShouldBe("application/vnd.2s.secret-reference");

        var conflict = await Set(new KeyValueSetCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, Mock.Of<IFileSystem>()) { Key = "x", Value = "kv://v/n", Reference = true, Protection = "standard" });
        conflict.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
    }

    [Fact]
    public async Task Set_Hyok_EncryptsLocallyForTheEnabledRecipients()
    {
        using var api = new TestRecipient("api");
        using var old = new TestRecipient("old");
        old.Model.IsEnabled = false;
        _api.Setup(a => a.GetRecipientsAsync("org1", "proj1")).ReturnsAsync([api.Model, old.Model]);

        var exitCode = await Set(new KeyValueSetCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, Mock.Of<IFileSystem>()) { Key = "Db.Password", Value = "plain", Protection = "hyok" });

        exitCode.ShouldBe(ExitCodes.SUCCESS);
        _written!.Protection.ShouldBe(KeyValueWriteProtection.Hyok);
        var document = JweDocument.Parse(_written.Value);
        document.Place.ShouldBe("org1/proj1/default/db.password");
        document.Recipients.Select(recipient => recipient.KeyId).ShouldBe(["api"]);
        api.Decrypt(_written.Value).ShouldBe("plain");
        _console.Lines.ShouldContain(line => line.Contains("Encrypted locally for 1 recipient"));
    }

    [Fact]
    public async Task Set_ValueFromAFile_AndTooManySources()
    {
        var files = KeyValueTestContext.Files(("value.txt", "from file"));

        await Set(new KeyValueSetCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, files) { Key = "k", ImportFilePath = "value.txt", Protection = "public" });
        _written!.Value.ShouldBe("from file");

        var exitCode = await Set(new KeyValueSetCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, files) { Key = "k", Value = "x", ImportFilePath = "value.txt", Protection = "public" });
        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
    }

    [Fact]
    public async Task Get_ValueOnly_PrintsTheValueAlone_AndAHiddenSecretIsAWarning()
    {
        _api.Setup(api => api.GetKeyValueAsync("org1", "proj1", "default", "db.host", null, null)).ReturnsAsync(KeyValueTestContext.Pair("db.host", KeyValueProtection.Public, "sql"));
        _api.Setup(api => api.GetKeyValueAsync("org1", "proj1", "default", "db.password", null, null)).ReturnsAsync(KeyValueTestContext.Pair("db.password", KeyValueProtection.Standard, null));

        var shown = await new KeyValueGetCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, Mock.Of<IFileSystem>()) { Key = "db.host", ValueOnly = true }.OnExecuteAsync();
        var hidden = await new KeyValueGetCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, Mock.Of<IFileSystem>()) { Key = "db.password", ValueOnly = true }.OnExecuteAsync();

        shown.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldContain("sql");
        hidden.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        _console.Lines.ShouldContain(line => line.Contains("add --reveal"));
    }

    [Fact]
    public async Task Get_Hyok_WithAPrivateKey_DecryptsLocally()
    {
        using var api = new TestRecipient("api");
        using var worker = new TestRecipient("worker");
        var document = ClientSecretEncryptor.Encrypt("plain", [new ClientEncryptionRecipientKey("api", RsaPublicJwk.FromRsa(api.Rsa)), new ClientEncryptionRecipientKey("worker", RsaPublicJwk.FromRsa(worker.Rsa))], "org1/proj1/default/db.password");
        _api.Setup(a => a.GetKeyValueAsync("org1", "proj1", "default", "db.password", true, null)).ReturnsAsync(() => KeyValueTestContext.Pair("db.password", KeyValueProtection.Hyok, document));

        var decrypted = await new KeyValueGetCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, Mock.Of<IFileSystem>()) { Key = "db.password", ValueOnly = true, PrivateKeyPath = worker.PrivateKeyPem, RecipientId = "worker" }.OnExecuteAsync();
        var ambiguous = await new KeyValueGetCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, Mock.Of<IFileSystem>()) { Key = "db.password", ValueOnly = true, PrivateKeyPath = worker.PrivateKeyPem }.OnExecuteAsync();

        decrypted.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldContain("plain");
        ambiguous.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.Lines.ShouldContain(line => line.Contains("--kid"));
    }

    [Fact]
    public async Task Get_Missing_ExplainsTheApiError()
    {
        _api.Setup(api => api.GetKeyValueAsync("org1", "proj1", "default", "nope", null, null)).ThrowsAsync(KeyValueTestContext.NotFound("nope"));

        var exitCode = await new KeyValueGetCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object, Mock.Of<IFileSystem>()) { Key = "nope" }.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.Lines.ShouldContain(line => line.Contains("does not exist"));
        _console.Lines.ShouldContain(line => line.Contains("KeyValueNotFound"));
    }

    private static Task<int> Set(KeyValueSetCommand command) => command.OnExecuteAsync();
}
