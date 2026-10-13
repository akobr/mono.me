using System.Net;
using System.Threading.Tasks;
using _42.Platform.Cli.Commands.KeyValues;
using _42.Platform.Storyteller.Sdk;
using Moq;
using Shouldly;
using Xunit;

namespace _42.Platform.Cli.UnitTests.Commands.KeyValues;

public class KeyValueBulkAndDeleteCommandTests
{
    private readonly RecordingConsole _console = new();
    private readonly Mock<IKeyValuesApiClient> _api = new();

    [Fact]
    public async Task Restore_SendsTheKeysWithoutALevel_AndPartialOutcomesWarn()
    {
        KeyValueBulkRequest? sent = null;
        _api.Setup(api => api.RestoreKeyValuesAsync("org1", "proj1", "default", It.IsAny<KeyValueBulkRequest>()))
            .Callback<string, string, string, KeyValueBulkRequest>((_, _, _, request) => sent = request)
            .ReturnsAsync(KeyValueTestContext.Bulk(("a", KeyValueBulkItemResultOutcome.Done), ("b", KeyValueBulkItemResultOutcome.NotFound)));

        var exitCode = await new KeyValueRestoreCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { Keys = ["a", "b"] }.OnExecuteAsync();

        exitCode.ShouldBe(ExitCodes.WARNING_PARTIAL_RESULT);
        sent!.Keys.ShouldBe(["a", "b"]);
        sent.KeyPrefix.ShouldBeNull();
        sent.Protection.ShouldBeNull();
        _console.Lines.ShouldContain(line => line.Contains("1 of 2 keys restored"));
    }

    [Fact]
    public async Task Bulk_NeedsKeysOrAPrefix_NotBoth()
    {
        var none = await new KeyValueRestoreCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object).OnExecuteAsync();
        var both = await new KeyValueRestoreCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { Keys = ["a"], Prefix = "db." }.OnExecuteAsync();

        none.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        both.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Purge_ByPrefix_AsksForTheViewName()
    {
        _api.Setup(api => api.PurgeKeyValuesAsync("org1", "proj1", "default", It.IsAny<KeyValueBulkRequest>()))
            .ReturnsAsync(KeyValueTestContext.Bulk(("a", KeyValueBulkItemResultOutcome.Done)));

        _console.Input("other");
        var aborted = await new KeyValuePurgeCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { Prefix = string.Empty }.OnExecuteAsync();
        _console.Input("default");
        var purged = await new KeyValuePurgeCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { Prefix = string.Empty }.OnExecuteAsync();
        var skipped = await new KeyValuePurgeCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { Keys = ["a"], Yes = true }.OnExecuteAsync();

        aborted.ShouldBe(ExitCodes.WARNING_ABORTED);
        purged.ShouldBe(ExitCodes.SUCCESS);
        skipped.ShouldBe(ExitCodes.SUCCESS);
        _api.Verify(api => api.PurgeKeyValuesAsync("org1", "proj1", "default", It.IsAny<KeyValueBulkRequest>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Protect_NeedsAServerSideLevel()
    {
        KeyValueBulkRequest? sent = null;
        _api.Setup(api => api.ProtectKeyValuesAsync("org1", "proj1", "default", It.IsAny<KeyValueBulkRequest>()))
            .Callback<string, string, string, KeyValueBulkRequest>((_, _, _, request) => sent = request)
            .ReturnsAsync(KeyValueTestContext.Bulk(("a", KeyValueBulkItemResultOutcome.Done)));

        var @public = await new KeyValueProtectCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { Keys = ["a"], Protection = "public" }.OnExecuteAsync();
        var hsm = await new KeyValueProtectCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { Prefix = "db.", Protection = "HSM" }.OnExecuteAsync();

        @public.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        hsm.ShouldBe(ExitCodes.SUCCESS);
        sent!.Protection.ShouldBe(KeyValueBulkRequestProtection.Hsm);
        sent.KeyPrefix.ShouldBe("db.");
    }

    [Fact]
    public async Task Delete_SoftTellsHowToRestore_PermanentAsksForTheKey()
    {
        var soft = await new KeyValueDeleteCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { Key = "db.host", ExpectedVersion = 3 }.OnExecuteAsync();
        _console.Input("something else");
        var aborted = await new KeyValueDeleteCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { Key = "db.host", Permanent = true }.OnExecuteAsync();
        _console.Input("DB.HOST");
        var permanent = await new KeyValueDeleteCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { Key = "db.host", Permanent = true }.OnExecuteAsync();

        soft.ShouldBe(ExitCodes.SUCCESS);
        _console.Lines.ShouldContain(line => line.Contains("sform story kv restore db.host"));
        aborted.ShouldBe(ExitCodes.WARNING_ABORTED);
        permanent.ShouldBe(ExitCodes.SUCCESS);
        _api.Verify(api => api.DeleteKeyValueAsync("org1", "proj1", "default", "db.host", null, 3), Times.Once);
        _api.Verify(api => api.DeleteKeyValueAsync("org1", "proj1", "default", "db.host", true, null), Times.Once);
    }

    [Fact]
    public async Task ApiErrors_BecomeMessagesAndExitCodes()
    {
        _api.Setup(api => api.DeleteKeyValueAsync("org1", "proj1", "default", "s", null, null))
            .ThrowsAsync(KeyValueTestContext.Error(HttpStatusCode.Forbidden, "Secret values are delivered to machines only over mTLS in this project.", "MtlsRequired"));
        _api.Setup(api => api.DeleteKeyValueAsync("org1", "proj1", "default", "v", null, null))
            .ThrowsAsync(KeyValueTestContext.Error(HttpStatusCode.PreconditionFailed, "The key 'v' is at version 2, not 1.", "KeyValueVersionMismatch"));
        _api.Setup(api => api.DeleteKeyValueAsync("org1", "proj1", "default", "x", null, null))
            .ThrowsAsync(KeyValueTestContext.Error(HttpStatusCode.ServiceUnavailable, "<html>", null));

        var mtls = await new KeyValueDeleteCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { Key = "s" }.OnExecuteAsync();
        var version = await new KeyValueDeleteCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { Key = "v" }.OnExecuteAsync();
        var down = await new KeyValueDeleteCommand(_console.Object, KeyValueTestContext.Create().Object, _api.Object) { Key = "x" }.OnExecuteAsync();

        mtls.ShouldBe(ExitCodes.WARNING_UNAUTHORIZED_ACCESS);
        _console.Lines.ShouldContain(line => line.Contains("machine certificate"));
        version.ShouldBe(ExitCodes.ERROR_WRONG_INPUT);
        _console.Lines.ShouldContain(line => line.Contains("KeyValueVersionMismatch"));
        down.ShouldBe(ExitCodes.ERROR_CRASH);
    }
}
