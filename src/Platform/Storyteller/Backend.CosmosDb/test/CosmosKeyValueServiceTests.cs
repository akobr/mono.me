#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.KeyValues.Protection;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Every test works in its own project of the test organization, so the pairs of other tests never interfere.
public class CosmosKeyValueServiceTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Org = TestConstants.Organization;
    private const string View = Constants.DefaultViewName;

    private static readonly KeyValueCaller Admin = KeyValueCaller.WithSecrets(AuditActor.Account("admin"));
    private static readonly KeyValueCaller Reader = KeyValueCaller.WithoutSecrets(AuditActor.Account("reader"));

    private IKeyValueService KeyValues => Context.Services.GetRequiredService<IKeyValueService>();

    private FakeKeyEncryptionProvider KeyProvider => Context.Services.GetRequiredService<FakeKeyEncryptionProvider>();

    private static string NewProject(string name) => $"kv-{name}-{Guid.NewGuid().ToString("N")[..12]}";

    private static KeyValueWrite Public(string value, string? contentType = null, ulong? expectedVersion = null) => new()
    {
        Protection = KeyValueProtection.Public,
        Value = value,
        ContentType = contentType,
        ExpectedVersion = expectedVersion,
    };

    private static KeyValueWrite Secret(string value, string? contentType = null, ulong? expectedVersion = null) => new()
    {
        Protection = KeyValueProtection.Standard,
        Value = value,
        ContentType = contentType,
        ExpectedVersion = expectedVersion,
    };

    [Fact]
    public async Task PublicLifecycle_VersionsContentAndDiff()
    {
        var project = NewProject("public");

        var v1 = await KeyValues.SetAsync(Org, project, View, "db.host", Public("sql-1"), Admin);
        var v2 = await KeyValues.SetAsync(Org, project, View, "db.host", Public("sql-2", "text/plain"), Admin);
        var same = await KeyValues.SetAsync(Org, project, View, "db.host", Public("sql-2", "text/plain"), Admin);
        var v3 = await KeyValues.SetAsync(Org, project, View, "DB.Host", new KeyValueWrite { Value = "sql-3" }, Admin);

        v1.Version.Should().Be(1);
        v2.Version.Should().Be(2);
        same.Version.Should().Be(2, "the same value and content type are not written again");
        v3.Version.Should().Be(3);
        v3.Key.Should().Be("db.host", "the casing of the first write is kept");
        v3.Protection.Should().Be(KeyValueProtection.Public, "an update without a level keeps the level");
        v3.ContentType.Should().BeNull("the content type is not carried over implicitly");
        v3.Created.By.Should().Be(AuditActor.Account("admin"));

        var current = await KeyValues.GetAsync(Org, project, View, "db.host", new KeyValueReadOptions(), Reader);
        current!.Value.Should().Be("sql-3");
        current.IsSecret.Should().BeFalse();

        var versions = await KeyValues.GetVersionsAsync(Org, project, View, "db.host");
        versions.Select(version => version.Version).Should().Equal(1u, 2u, 3u);
        versions.Select(version => version.IsCurrent).Should().Equal(false, false, true);
        versions[0].ExpirationTime.Should().BeAfter(DateTimeOffset.UtcNow.AddDays(300));
        versions[2].ExpirationTime.Should().BeNull("the current version of an active key never expires");

        (await KeyValues.GetVersionAsync(Org, project, View, "db.host", 1, reveal: false, Reader))!.Value.Should().Be("sql-1");
        (await KeyValues.GetVersionAsync(Org, project, View, "db.host", 2, reveal: false, Reader))!.ContentType.Should().Be("text/plain");
        (await KeyValues.GetVersionAsync(Org, project, View, "db.host", 3, reveal: false, Reader))!.Value.Should().Be("sql-3");
        (await KeyValues.GetVersionAsync(Org, project, View, "db.host", 4, reveal: false, Reader)).Should().BeNull();

        var diff = await KeyValues.GetVersionChangesAsync(Org, project, View, "db.host", 1, 2, Reader);
        diff.Stats.Additions.Should().Be(1);
        diff.Stats.Deletions.Should().Be(1);

        var fromNothing = await KeyValues.GetVersionChangesAsync(Org, project, View, "db.host", 0, 1, Reader);
        fromNothing.Stats.Additions.Should().Be(1);
        fromNothing.Stats.Deletions.Should().Be(0);

        var unknown = () => KeyValues.GetVersionChangesAsync(Org, project, View, "db.host", 1, 9, Reader);
        await unknown.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteAndRestore_KeepTheVersionAndContinueTheHistory()
    {
        var project = NewProject("delete");
        await KeyValues.SetAsync(Org, project, View, "a.one", Public("1"), Admin);
        await KeyValues.SetAsync(Org, project, View, "a.one", Public("2"), Admin);
        await KeyValues.SetAsync(Org, project, View, "a.two", Secret("s"), Admin);
        await KeyValues.SetAsync(Org, project, View, "b.one", Public("b"), Admin);

        (await KeyValues.DeleteAsync(Org, project, View, "a.one", new KeyValueDeleteOptions(), Admin)).Should().BeTrue();
        (await KeyValues.DeleteAsync(Org, project, View, "a.one", new KeyValueDeleteOptions(), Admin)).Should().BeFalse("already deleted");
        (await KeyValues.DeleteAsync(Org, project, View, "a.two", new KeyValueDeleteOptions(), Admin)).Should().BeTrue();
        (await KeyValues.DeleteAsync(Org, project, View, "missing", new KeyValueDeleteOptions(), Admin)).Should().BeFalse();

        (await KeyValues.GetAsync(Org, project, View, "a.one", new KeyValueReadOptions(), Reader)).Should().BeNull();
        var deleted = await KeyValues.GetAsync(Org, project, View, "a.one", new KeyValueReadOptions { IncludeDeleted = true }, Reader);
        deleted!.IsDeleted.Should().BeTrue();
        deleted.Deleted!.By.Should().Be(AuditActor.Account("admin"));
        deleted.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow.AddDays(80));
        deleted.Version.Should().Be(2);

        var active = await KeyValues.ListAsync(Org, project, View, new KeyValueListRequest());
        active.Items.Select(item => item.Key).Should().Equal("b.one");

        var deletedList = await KeyValues.ListAsync(Org, project, View, new KeyValueListRequest { State = KeyValueState.Deleted });
        deletedList.Items.Select(item => item.Key).Should().Equal("a.one", "a.two");
        deletedList.Items.Should().OnlyContain(item => item.IsDeleted && item.ExpiresAt != null);

        var versionsWhileDeleted = await KeyValues.GetVersionsAsync(Org, project, View, "a.one");
        versionsWhileDeleted.Select(version => version.Version).Should().Equal(1u, 2u);
        versionsWhileDeleted.Should().OnlyContain(version => version.ExpirationTime != null, "the delete wrote the history of the current version");
        versionsWhileDeleted[1].IsCurrent.Should().BeTrue();

        var restore = await KeyValues.RestoreAsync(Org, project, View, new KeyValueBulkRequest { Keys = ["a.one", "a.two", "b.one", "nope"] }, Admin);
        restore.Items.Select(item => (item.Key, item.Outcome)).Should().Equal(
            ("a.one", KeyValueBulkOutcome.Done),
            ("a.two", KeyValueBulkOutcome.Done),
            ("b.one", KeyValueBulkOutcome.Unchanged),
            ("nope", KeyValueBulkOutcome.NotFound));
        restore.Items[0].Version.Should().Be(2, "a restore creates no version");

        var restored = await KeyValues.GetAsync(Org, project, View, "a.one", new KeyValueReadOptions(), Reader);
        restored!.Version.Should().Be(2);
        restored.IsDeleted.Should().BeFalse();
        restored.Value.Should().Be("2");

        var versionsAfterRestore = await KeyValues.GetVersionsAsync(Org, project, View, "a.one");
        versionsAfterRestore.Select(version => version.Version).Should().Equal(1u, 2u);
        versionsAfterRestore[1].ExpirationTime.Should().BeNull("the current version is active again");

        // a write to a deleted key continues the numbering, and the old versions are still there
        await KeyValues.DeleteAsync(Org, project, View, "a.one", new KeyValueDeleteOptions(), Admin);
        var v3 = await KeyValues.SetAsync(Org, project, View, "a.one", Public("3", expectedVersion: 0), Admin);
        v3.Version.Should().Be(3);
        v3.IsDeleted.Should().BeFalse();
        (await KeyValues.GetVersionsAsync(Org, project, View, "a.one")).Select(version => version.Version).Should().Equal(1u, 2u, 3u);
    }

    [Fact]
    public async Task ExpiredDeletedKey_IsGoneAndANewCreateContinuesAfterItsVersions()
    {
        var project = NewProject("expiry");
        var shortLived = CreateService(versionRetention: TimeSpan.FromSeconds(40), deletedRetention: TimeSpan.FromSeconds(3));

        await shortLived.SetAsync(Org, project, View, "temp.key", Public("1"), Admin);
        await shortLived.SetAsync(Org, project, View, "temp.key", Public("2"), Admin);
        await shortLived.DeleteAsync(Org, project, View, "temp.key", new KeyValueDeleteOptions(), Admin);

        await WaitUntilAsync(
            async () => await shortLived.GetAsync(Org, project, View, "temp.key", new KeyValueReadOptions { IncludeDeleted = true }, Reader) is null,
            TimeSpan.FromSeconds(60));

        var versions = await shortLived.GetVersionsAsync(Org, project, View, "temp.key");
        versions.Select(version => version.Version).Should().Equal([1u, 2u], "the versions outlive the deleted key");

        var recreated = await shortLived.SetAsync(Org, project, View, "temp.key", Public("3"), Admin);
        recreated.Version.Should().Be(3);
        recreated.Created.By.Should().Be(AuditActor.Account("admin"));
    }

    [Fact]
    public async Task HistoryVersion_ExpiresAfterTheVersionRetention()
    {
        var project = NewProject("version-ttl");
        var shortLived = CreateService(versionRetention: TimeSpan.FromSeconds(3), deletedRetention: TimeSpan.FromSeconds(60));

        await shortLived.SetAsync(Org, project, View, "k", Public("1"), Admin);
        await shortLived.SetAsync(Org, project, View, "k", Public("2"), Admin);

        await WaitUntilAsync(
            async () => (await shortLived.GetVersionsAsync(Org, project, View, "k")).Count == 1,
            TimeSpan.FromSeconds(60));

        var versions = await shortLived.GetVersionsAsync(Org, project, View, "k");
        versions.Single().Version.Should().Be(2);
        versions.Single().IsCurrent.Should().BeTrue();
        (await shortLived.GetAsync(Org, project, View, "k", new KeyValueReadOptions(), Reader))!.Value.Should().Be("2", "the current version never expires");
    }

    [Fact]
    public async Task PermanentDeleteAndPurge_RemoveEverything()
    {
        var project = NewProject("purge");
        await KeyValues.SetAsync(Org, project, View, "p.one", Public("1"), Admin);
        await KeyValues.SetAsync(Org, project, View, "p.one", Public("2"), Admin);
        await KeyValues.SetAsync(Org, project, View, "p.two", Secret("s1"), Admin);
        await KeyValues.SetAsync(Org, project, View, "p.two", Secret("s2"), Admin);
        await KeyValues.SetAsync(Org, project, View, "p.three", Public("3"), Admin);

        (await KeyValues.DeleteAsync(Org, project, View, "p.one", new KeyValueDeleteOptions { Permanent = true }, Admin)).Should().BeTrue();
        (await KeyValues.GetVersionsAsync(Org, project, View, "p.one")).Should().BeEmpty();
        (await KeyValues.SetAsync(Org, project, View, "p.one", Public("again"), Admin)).Version.Should().Be(1, "nothing is left after a permanent delete");

        await KeyValues.DeleteAsync(Org, project, View, "p.two", new KeyValueDeleteOptions(), Admin);
        var purge = await KeyValues.PurgeAsync(Org, project, View, new KeyValueBulkRequest { Keys = ["p.two", "p.three", "p.none"] }, Admin);
        purge.Items.Select(item => item.Outcome).Should().Equal(KeyValueBulkOutcome.Done, KeyValueBulkOutcome.NotFound, KeyValueBulkOutcome.NotFound);

        (await KeyValues.GetAsync(Org, project, View, "p.two", new KeyValueReadOptions { IncludeDeleted = true }, Admin)).Should().BeNull();
        (await KeyValues.GetVersionsAsync(Org, project, View, "p.two")).Should().BeEmpty();
        (await KeyValues.GetAsync(Org, project, View, "p.three", new KeyValueReadOptions(), Admin)).Should().NotBeNull("an active key is not purged");
    }

    [Fact]
    public async Task PermanentDelete_ManyVersions_RemovesAllBatches()
    {
        var project = NewProject("purge-many");

        for (var i = 0; i < 150; i++)
        {
            await KeyValues.SetAsync(Org, project, View, "many", Public(i.ToString()), Admin);
        }

        (await KeyValues.GetVersionsAsync(Org, project, View, "many")).Should().HaveCount(150);
        (await KeyValues.DeleteAsync(Org, project, View, "many", new KeyValueDeleteOptions { Permanent = true }, Admin)).Should().BeTrue();
        (await KeyValues.GetVersionsAsync(Org, project, View, "many")).Should().BeEmpty();
    }

    [Fact]
    public async Task Secrets_AreEncryptedAtRestAndRevealedOnlyWithRights()
    {
        var project = NewProject("secret");
        var written = await KeyValues.SetAsync(Org, project, View, "db.password", Secret("hunter2", "text/plain"), Admin);

        written.IsSecret.Should().BeTrue();
        written.Value.Should().Be("hunter2", "the write returns what was written");

        var hidden = await KeyValues.GetAsync(Org, project, View, "db.password", new KeyValueReadOptions(), Reader);
        hidden!.Value.Should().BeNull();
        hidden.Protection.Should().Be(KeyValueProtection.Standard);

        var reveal = () => KeyValues.GetAsync(Org, project, View, "db.password", new KeyValueReadOptions { Reveal = true }, Reader);
        await reveal.Should().ThrowAsync<AccessDeniedException>();

        var revealed = await KeyValues.GetAsync(Org, project, View, "db.password", new KeyValueReadOptions { Reveal = true }, Admin);
        revealed!.Value.Should().Be("hunter2");

        var raw = await ReadRawItemAsync(project, "kv.db.password");
        raw.Should().NotContain("hunter2", "the catalog item holds no plaintext");
        raw.Should().Contain("\"Encrypted\"");

        var listed = await KeyValues.ListAsync(Org, project, View, new KeyValueListRequest { IncludeValues = true });
        listed.Items.Single().Value.Should().BeNull("values of secrets are never listed");

        var writeWithoutRights = () => KeyValues.SetAsync(Org, project, View, "db.password", Secret("x"), Reader);
        await writeWithoutRights.Should().ThrowAsync<AccessDeniedException>();
        var createWithoutRights = () => KeyValues.SetAsync(Org, project, View, "other.secret", Secret("x"), Reader);
        await createWithoutRights.Should().ThrowAsync<AccessDeniedException>();
        var toSecretWithoutRights = () => KeyValues.SetAsync(Org, project, View, "db.password", Public("x"), Reader);
        await toSecretWithoutRights.Should().ThrowAsync<AccessDeniedException>("a secret becoming public needs secret rights too");
        var deleteWithoutRights = () => KeyValues.DeleteAsync(Org, project, View, "db.password", new KeyValueDeleteOptions(), Reader);
        await deleteWithoutRights.Should().ThrowAsync<AccessDeniedException>();

        // the version history of a secret works like for a public pair, with the same rules
        await KeyValues.SetAsync(Org, project, View, "db.password", Secret("hunter3"), Admin);
        (await KeyValues.GetVersionAsync(Org, project, View, "db.password", 1, reveal: true, Admin))!.Value.Should().Be("hunter2");
        (await KeyValues.GetVersionAsync(Org, project, View, "db.password", 1, reveal: false, Reader))!.Value.Should().BeNull();
        var diffWithoutRights = () => KeyValues.GetVersionChangesAsync(Org, project, View, "db.password", 1, 2, Reader);
        await diffWithoutRights.Should().ThrowAsync<AccessDeniedException>();
        (await KeyValues.GetVersionChangesAsync(Org, project, View, "db.password", 1, 2, Admin)).Stats.Additions.Should().Be(1);
    }

    [Fact]
    public async Task Secrets_CiphertextIsBoundToItsPlaceAndCached()
    {
        var project = NewProject("integrity");
        var protector = Context.Services.GetRequiredService<IValueProtector>();
        var identity = new KeyValueIdentity(Org, project, View, "bound.key", 1);
        var stored = await protector.ProtectAsync(identity, KeyValueProtection.Standard, "value", null);

        (await protector.UnprotectAsync(identity, stored)).Should().Be("value");

        var movedKey = () => protector.UnprotectAsync(identity with { Key = "other.key" }, stored);
        await movedKey.Should().ThrowAsync<KeyValueIntegrityException>();
        var movedView = () => protector.UnprotectAsync(identity with { View = "rollout" }, stored);
        await movedView.Should().ThrowAsync<KeyValueIntegrityException>();
        var movedVersion = () => protector.UnprotectAsync(identity with { Version = 2 }, stored);
        await movedVersion.Should().ThrowAsync<KeyValueIntegrityException>();

        await KeyValues.SetAsync(Org, project, View, "cached", Secret("plain"), Admin);
        var before = KeyProvider.UnwrapCount;
        await KeyValues.GetAsync(Org, project, View, "cached", new KeyValueReadOptions { Reveal = true }, Admin);
        await KeyValues.GetAsync(Org, project, View, "cached", new KeyValueReadOptions { Reveal = true }, Admin);
        (KeyProvider.UnwrapCount - before).Should().BeLessThanOrEqualTo(1, "the plaintext of a version is cached");
    }

    [Theory]
    [InlineData("db-host")]
    [InlineData("1st")]
    [InlineData("a..b")]
    [InlineData("a.")]
    [InlineData(".a")]
    [InlineData("")]
    [InlineData("a b")]
    public async Task InvalidKeys_AreRejected(string key)
    {
        var project = NewProject("keys");
        var act = () => KeyValues.SetAsync(Org, project, View, key, Public("x"), Admin);
        var exception = await act.Should().ThrowAsync<InvalidInputException>();
        exception.Which.ErrorCode.Should().Be(ErrorCodes.InvalidKey);
    }

    [Fact]
    public async Task Values_AreLimitedTo24576Bytes()
    {
        var project = NewProject("size");
        var limit = new string('x', 24_576);
        (await KeyValues.SetAsync(Org, project, View, "big", Public(limit), Admin)).Value.Should().HaveLength(24_576);

        var tooBig = () => KeyValues.SetAsync(Org, project, View, "bigger", Public(limit + "y"), Admin);
        (await tooBig.Should().ThrowAsync<InvalidInputException>()).Which.ErrorCode.Should().Be(ErrorCodes.ValueTooLarge);

        var multiByte = () => KeyValues.SetAsync(Org, project, View, "utf8", Public(new string('é', 12_289)), Admin);
        await multiByte.Should().ThrowAsync<InvalidInputException>("the limit is in UTF-8 bytes");

        var create = () => KeyValues.SetAsync(Org, project, View, "no.level", new KeyValueWrite { Value = "x" }, Admin);
        (await create.Should().ThrowAsync<InvalidInputException>()).Which.ErrorCode.Should().Be(ErrorCodes.ProtectionRequired);

        var notAllowed = () => KeyValues.SetAsync(Org, project, View, "hsm", new KeyValueWrite { Protection = KeyValueProtection.Hsm, Value = "x" }, Admin);
        (await notAllowed.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.ProtectionNotAllowed);
    }

    [Fact]
    public async Task ExpectedVersion_IsChecked()
    {
        var project = NewProject("expected");

        var mismatch = () => KeyValues.SetAsync(Org, project, View, "k", Public("1", expectedVersion: 1), Admin);
        (await mismatch.Should().ThrowAsync<PreconditionFailedException>()).Which.ErrorCode.Should().Be(ErrorCodes.KeyValueVersionMismatch);

        await KeyValues.SetAsync(Org, project, View, "k", Public("1", expectedVersion: 0), Admin);
        await KeyValues.SetAsync(Org, project, View, "k", Public("2", expectedVersion: 1), Admin);

        var stale = () => KeyValues.SetAsync(Org, project, View, "k", Public("3", expectedVersion: 1), Admin);
        await stale.Should().ThrowAsync<PreconditionFailedException>();
        var zeroOnActive = () => KeyValues.SetAsync(Org, project, View, "k", Public("3", expectedVersion: 0), Admin);
        await zeroOnActive.Should().ThrowAsync<PreconditionFailedException>();

        var deleteStale = () => KeyValues.DeleteAsync(Org, project, View, "k", new KeyValueDeleteOptions { ExpectedVersion = 1 }, Admin);
        await deleteStale.Should().ThrowAsync<PreconditionFailedException>();
        (await KeyValues.DeleteAsync(Org, project, View, "k", new KeyValueDeleteOptions { ExpectedVersion = 2 }, Admin)).Should().BeTrue();

        // a deleted key accepts 0 and its version
        (await KeyValues.SetAsync(Org, project, View, "k", Public("3", expectedVersion: 2), Admin)).Version.Should().Be(3);
    }

    [Fact]
    public async Task ConcurrentWrites_ProduceConsecutiveVersions()
    {
        var project = NewProject("concurrent");
        await KeyValues.SetAsync(Org, project, View, "race", Public("0"), Admin);

        await Task.WhenAll(Enumerable.Range(1, 8).Select(i =>
            KeyValues.SetAsync(Org, project, View, "race", Public($"v{i}"), KeyValueCaller.WithSecrets(AuditActor.Account($"writer-{i}")))));

        var versions = await KeyValues.GetVersionsAsync(Org, project, View, "race");
        versions.Select(version => version.Version).Should().Equal(Enumerable.Range(1, 9).Select(i => (ulong)i));
        (await KeyValues.GetAsync(Org, project, View, "race", new KeyValueReadOptions(), Reader))!.Version.Should().Be(9);
    }

    [Fact]
    public async Task Listing_FiltersPagesAndIsolatesViews()
    {
        var project = NewProject("list");
        await KeyValues.SetAsync(Org, project, View, "app.name", Public("app", "text/plain"), Admin);
        await KeyValues.SetAsync(Org, project, View, "app.token", Secret("t"), Admin);
        await KeyValues.SetAsync(Org, project, View, "db.host", Public("sql"), Admin);
        await KeyValues.SetAsync(Org, project, View, "ref.vault", Public("kv://vault/name", KeyValueContentTypes.SecretReference), Admin);
        await KeyValues.SetAsync(Org, project, "rollout", "app.name", Public("rollout-app"), Admin);

        var all = await KeyValues.ListAsync(Org, project, View, new KeyValueListRequest { IncludeValues = true });
        all.Items.Select(item => item.Key).Should().Equal("app.name", "app.token", "db.host", "ref.vault");
        all.Items.Select(item => item.Value).Should().Equal("app", null, "sql", "kv://vault/name");
        all.Items[0].ContentType.Should().Be("text/plain");
        all.Items[3].IsReference.Should().BeTrue();
        all.Count.Should().Be(4);
        all.ContinuationToken.Should().BeNull();

        var withoutValues = await KeyValues.ListAsync(Org, project, View, new KeyValueListRequest());
        withoutValues.Items.Should().OnlyContain(item => item.Value == null);

        var byPrefix = await KeyValues.ListAsync(Org, project, View, new KeyValueListRequest { KeyPrefix = "APP." });
        byPrefix.Items.Select(item => item.Key).Should().Equal("app.name", "app.token");

        var secrets = await KeyValues.ListAsync(Org, project, View, new KeyValueListRequest { Protections = [KeyValueProtection.Standard] });
        secrets.Items.Select(item => item.Key).Should().Equal("app.token");

        var otherView = await KeyValues.ListAsync(Org, project, "rollout", new KeyValueListRequest { IncludeValues = true });
        otherView.Items.Single().Value.Should().Be("rollout-app");
        (await KeyValues.GetAsync(Org, project, "rollout", "db.host", new KeyValueReadOptions(), Reader)).Should().BeNull("views are isolated");
    }

    [Fact]
    public async Task Listing_Pages()
    {
        var project = NewProject("paging");
        var document = new KeyValueDocument
        {
            Items = Enumerable.Range(0, 1_050).Select(i => new KeyValueDocumentItem
            {
                Key = $"k{i:D4}",
                Protection = KeyValueProtection.Public,
                Value = i.ToString(),
            }).ToList(),
        };

        var first = await KeyValues.ImportAsync(Org, project, View, new KeyValueDocument { Items = document.Items.Take(1_000).ToList() }, new KeyValueImportOptions(), Admin);
        first.IsComplete.Should().BeTrue();
        var second = await KeyValues.ImportAsync(Org, project, View, new KeyValueDocument { Items = document.Items.Skip(1_000).ToList() }, new KeyValueImportOptions(), Admin);
        second.IsComplete.Should().BeTrue();

        var page1 = await KeyValues.ListAsync(Org, project, View, new KeyValueListRequest());
        page1.Items.Should().HaveCount(CosmosConstants.MaxItemCountPerPage);
        page1.ContinuationToken.Should().NotBeNullOrWhiteSpace();

        var page2 = await KeyValues.ListAsync(Org, project, View, new KeyValueListRequest { ContinuationToken = page1.ContinuationToken });
        page2.Items.Should().HaveCount(50);
        page2.ContinuationToken.Should().BeNull();
        page1.Items.Concat(page2.Items).Select(item => item.Key).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Import_ValidatesPlansAndWrites()
    {
        var project = NewProject("import");
        await KeyValues.SetAsync(Org, project, View, "keep.me", Public("keep"), Admin);
        await KeyValues.SetAsync(Org, project, View, "old.secret", Secret("old"), Admin);
        await KeyValues.SetAsync(Org, project, View, "gone.key", Public("gone"), Admin);
        await KeyValues.DeleteAsync(Org, project, View, "gone.key", new KeyValueDeleteOptions(), Admin);

        var invalid = new KeyValueDocument
        {
            Items =
            [
                new() { Key = "bad-key", Protection = KeyValueProtection.Public, Value = "x" },
                new() { Key = "dup", Protection = KeyValueProtection.Public, Value = "1" },
                new() { Key = "DUP", Protection = KeyValueProtection.Public, Value = "2" },
                new() { Key = "new.secret", Protection = KeyValueProtection.Standard },
                new() { Key = "fine", Protection = KeyValueProtection.Public, Value = "fine" },
            ],
        };

        var act = () => KeyValues.ImportAsync(Org, project, View, invalid, new KeyValueImportOptions(), Admin);
        var failure = await act.Should().ThrowAsync<KeyValueImportInvalidException>();
        failure.Which.Items.Select(item => (item.Key, item.ErrorCode)).Should().BeEquivalentTo(
        [
            ("bad-key", ErrorCodes.InvalidKey),
            ("DUP", ErrorCodes.ImportInvalid),
            ("new.secret", ErrorCodes.SecretValueMissing),
        ]);
        (await KeyValues.GetAsync(Org, project, View, "fine", new KeyValueReadOptions(), Reader)).Should().BeNull("nothing is written when an item is invalid");

        var document = new KeyValueDocument
        {
            Items =
            [
                new() { Key = "keep.me", Protection = KeyValueProtection.Public, Value = "keep" },
                new() { Key = "old.secret", Protection = KeyValueProtection.Standard },
                new() { Key = "gone.key", Protection = KeyValueProtection.Public, Value = "back" },
                new() { Key = "fresh.one", Protection = KeyValueProtection.Public, Value = "1", ContentType = "text/plain" },
                new() { Key = "fresh.secret", Protection = KeyValueProtection.Standard, Value = "s" },
            ],
        };

        var dryRun = await KeyValues.ImportAsync(Org, project, View, document, new KeyValueImportOptions { DryRun = true }, Admin);
        dryRun.IsDryRun.Should().BeTrue();
        dryRun.Items.Select(item => (item.Key, item.Action)).Should().Equal(
            ("keep.me", KeyValueImportAction.Unchanged),
            ("old.secret", KeyValueImportAction.Unchanged),
            ("gone.key", KeyValueImportAction.Create),
            ("fresh.one", KeyValueImportAction.Create),
            ("fresh.secret", KeyValueImportAction.Create));
        (await KeyValues.GetAsync(Org, project, View, "fresh.one", new KeyValueReadOptions(), Reader)).Should().BeNull("a dry run writes nothing");

        var result = await KeyValues.ImportAsync(Org, project, View, document, new KeyValueImportOptions(), Admin);
        result.IsComplete.Should().BeTrue();
        result.Items.Select(item => item.Version).Should().Equal(1u, 1u, 2u, 1u, 1u);

        (await KeyValues.GetAsync(Org, project, View, "gone.key", new KeyValueReadOptions(), Reader))!.Value.Should().Be("back");
        (await KeyValues.GetVersionsAsync(Org, project, View, "gone.key")).Select(version => version.Version).Should().Equal(1u, 2u);
        (await KeyValues.GetAsync(Org, project, View, "old.secret", new KeyValueReadOptions { Reveal = true }, Admin))!.Value.Should().Be("old", "a secret without a value is left alone");
        (await KeyValues.GetAsync(Org, project, View, "fresh.secret", new KeyValueReadOptions { Reveal = true }, Admin))!.Value.Should().Be("s");

        var again = await KeyValues.ImportAsync(Org, project, View, document, new KeyValueImportOptions(), Admin);
        again.Items.Should().OnlyContain(item => item.Action == KeyValueImportAction.Unchanged, "a second run changes nothing");

        var readerImport = () => KeyValues.ImportAsync(Org, project, View, document, new KeyValueImportOptions(), Reader);
        await readerImport.Should().ThrowAsync<AccessDeniedException>("the import writes a secret");
    }

    // A caller who writes secrets but may not read them (an actor under NoSecretReveal) learns nothing from a guess: a set
    // of the stored value writes a new version, and an import, also a dry run, reports Update. Public values keep the
    // comparison, and a caller who reads secrets keeps it for secrets too.
    [Fact]
    public async Task SetAndImport_OfTheStoredSecret_ByACallerWhoMayNotReadSecrets_AreUpdates()
    {
        var project = NewProject("guess");
        var writer = new KeyValueCaller(AuditActor.Account("writer"), CanReadSecrets: false, CanWriteSecrets: true);
        await KeyValues.SetAsync(Org, project, View, "db.password", Secret("hunter2"), Admin);
        await KeyValues.SetAsync(Org, project, View, "db.host", Public("sql-1"), Admin);

        var document = new KeyValueDocument
        {
            Items =
            [
                new() { Key = "db.password", Protection = KeyValueProtection.Standard, Value = "hunter2" },
                new() { Key = "db.host", Protection = KeyValueProtection.Public, Value = "sql-1" },
            ],
        };

        var dryRun = await KeyValues.ImportAsync(Org, project, View, document, new KeyValueImportOptions { DryRun = true }, writer);
        dryRun.Items.Select(item => (item.Key, item.Action)).Should().Equal(
            ("db.password", KeyValueImportAction.Update),
            ("db.host", KeyValueImportAction.Unchanged));

        var adminDryRun = await KeyValues.ImportAsync(Org, project, View, document, new KeyValueImportOptions { DryRun = true }, Admin);
        adminDryRun.Items.Should().OnlyContain(item => item.Action == KeyValueImportAction.Unchanged, "a caller who reads secrets compares them");

        var sameSecret = await KeyValues.SetAsync(Org, project, View, "db.password", Secret("hunter2"), writer);
        var samePublic = await KeyValues.SetAsync(Org, project, View, "db.host", Public("sql-1"), writer);
        sameSecret.Version.Should().Be(2, "the secret is not compared for a caller who may not read it");
        samePublic.Version.Should().Be(1, "public values keep the comparison");

        var imported = await KeyValues.ImportAsync(Org, project, View, document, new KeyValueImportOptions(), writer);
        imported.Items.Select(item => (item.Key, item.Action, item.Version)).Should().Equal(
            ("db.password", KeyValueImportAction.Update, (ulong?)3),
            ("db.host", KeyValueImportAction.Unchanged, (ulong?)1));

        var adminSet = await KeyValues.SetAsync(Org, project, View, "db.password", Secret("hunter2"), Admin);
        adminSet.Version.Should().Be(3, "a caller who reads secrets does not write the same value again");
        (await KeyValues.GetAsync(Org, project, View, "db.password", new KeyValueReadOptions { Reveal = true }, Admin))!.Value.Should().Be("hunter2");
    }

    [Fact]
    public async Task Import_ReplaceDeletesTheRestUnderThePrefix()
    {
        var project = NewProject("replace");
        await KeyValues.SetAsync(Org, project, View, "app.a", Public("a"), Admin);
        await KeyValues.SetAsync(Org, project, View, "app.b", Public("b"), Admin);
        await KeyValues.SetAsync(Org, project, View, "other.c", Public("c"), Admin);

        var outside = () => KeyValues.ImportAsync(Org, project, View, new KeyValueDocument
        {
            Items = [new() { Key = "other.c", Protection = KeyValueProtection.Public, Value = "c" }],
        }, new KeyValueImportOptions { Mode = KeyValueImportMode.Replace, KeyPrefix = "app." }, Admin);
        await outside.Should().ThrowAsync<KeyValueImportInvalidException>();

        var result = await KeyValues.ImportAsync(Org, project, View, new KeyValueDocument
        {
            Items = [new() { Key = "app.a", Protection = KeyValueProtection.Public, Value = "a2" }],
        }, new KeyValueImportOptions { Mode = KeyValueImportMode.Replace, KeyPrefix = "app." }, Admin);

        result.Items.Select(item => (item.Key, item.Action)).Should().Equal(("app.a", KeyValueImportAction.Update), ("app.b", KeyValueImportAction.Delete));
        var active = await KeyValues.ListAsync(Org, project, View, new KeyValueListRequest());
        active.Items.Select(item => item.Key).Should().Equal("app.a", "other.c");

        var restored = await KeyValues.RestoreAsync(Org, project, View, new KeyValueBulkRequest { KeyPrefix = "app." }, Admin);
        restored.Items.Select(item => (item.Key, item.Outcome)).Should().Equal(("app.b", KeyValueBulkOutcome.Done));

        var everything = await KeyValues.ImportAsync(Org, project, View, new KeyValueDocument { Items = [] }, new KeyValueImportOptions { Mode = KeyValueImportMode.Replace }, Admin);
        everything.Items.Should().OnlyContain(item => item.Action == KeyValueImportAction.Delete);
        (await KeyValues.ListAsync(Org, project, View, new KeyValueListRequest())).Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_ManyItems_WritesInBatches()
    {
        var project = NewProject("batches");
        var items = Enumerable.Range(0, 120).Select(i => new KeyValueDocumentItem
        {
            Key = $"item.n{i}",
            Protection = i % 10 == 0 ? KeyValueProtection.Standard : KeyValueProtection.Public,
            Value = $"value-{i}",
        }).ToList();

        var created = await KeyValues.ImportAsync(Org, project, View, new KeyValueDocument { Items = items }, new KeyValueImportOptions(), Admin);
        created.IsComplete.Should().BeTrue();
        created.Items.Should().OnlyContain(item => item.Action == KeyValueImportAction.Create && item.Version == 1);

        var updated = await KeyValues.ImportAsync(Org, project, View, new KeyValueDocument
        {
            Items = items.Select(item => item with { Value = item.Value + "!" }).ToList(),
        }, new KeyValueImportOptions(), Admin);
        updated.Items.Should().OnlyContain(item => item.Action == KeyValueImportAction.Update && item.Version == 2);

        (await KeyValues.ListAsync(Org, project, View, new KeyValueListRequest())).Items.Should().HaveCount(120);
        (await KeyValues.GetAsync(Org, project, View, "item.n10", new KeyValueReadOptions { Reveal = true }, Admin))!.Value.Should().Be("value-10!");
        (await KeyValues.GetVersionsAsync(Org, project, View, "item.n7")).Should().HaveCount(2);
    }

    [Fact]
    public async Task Export_HidesSecretsUnlessAskedAndRoundTripsIntoAnotherView()
    {
        var project = NewProject("export");
        await KeyValues.SetAsync(Org, project, View, "x.public", Public("p", "text/plain"), Admin);
        await KeyValues.SetAsync(Org, project, View, "x.secret", Secret("s"), Admin);
        await KeyValues.SetAsync(Org, project, View, "x.deleted", Public("d"), Admin);
        await KeyValues.DeleteAsync(Org, project, View, "x.deleted", new KeyValueDeleteOptions(), Admin);

        var hidden = await KeyValues.ExportAsync(Org, project, View, new KeyValueExportRequest(), Reader);
        hidden.Format.Should().Be(KeyValueDocument.CurrentFormat);
        hidden.Items.Select(item => (item.Key, item.Value)).Should().Equal(("x.public", "p"), ("x.secret", null));

        var forbidden = () => KeyValues.ExportAsync(Org, project, View, new KeyValueExportRequest { IncludeSecrets = true }, Reader);
        await forbidden.Should().ThrowAsync<AccessDeniedException>();

        var full = await KeyValues.ExportAsync(Org, project, View, new KeyValueExportRequest { IncludeSecrets = true }, Admin);
        full.Items.Select(item => item.Value).Should().Equal("p", "s");

        var imported = await KeyValues.ImportAsync(Org, project, "rollout", full, new KeyValueImportOptions(), Admin);
        imported.IsComplete.Should().BeTrue();
        (await KeyValues.GetAsync(Org, project, "rollout", "x.secret", new KeyValueReadOptions { Reveal = true }, Admin))!.Value.Should().Be("s");
        (await KeyValues.GetAsync(Org, project, "rollout", "x.public", new KeyValueReadOptions(), Reader))!.ContentType.Should().Be("text/plain");
    }

    [Fact]
    public async Task Protect_ChangesTheLevelOfServerSideSecretsOnly()
    {
        var project = NewProject("protect");
        await KeyValues.SetAsync(Org, project, View, "pub", Public("p"), Admin);
        await KeyValues.SetAsync(Org, project, View, "sec", Secret("s"), Admin);

        var result = await KeyValues.ProtectAsync(Org, project, View, new KeyValueBulkRequest
        {
            Keys = ["pub", "sec", "none"],
            Protection = KeyValueProtection.Standard,
        }, Admin);

        result.Items.Select(item => (item.Key, item.Outcome)).Should().Equal(
            ("pub", KeyValueBulkOutcome.NotAllowed),
            ("sec", KeyValueBulkOutcome.Unchanged),
            ("none", KeyValueBulkOutcome.NotFound));
        result.Items[0].ErrorCode.Should().Be(ErrorCodes.ReprotectNotPossible);

        var missingLevel = () => KeyValues.ProtectAsync(Org, project, View, new KeyValueBulkRequest { Keys = ["sec"] }, Admin);
        await missingLevel.Should().ThrowAsync<InvalidInputException>();
    }

    [Fact]
    public async Task ViewDiscovery_FindsAViewThatHoldsOnlyPairs()
    {
        var project = NewProject("discover");
        await KeyValues.SetAsync(Org, project, "pairs-only", "k", Public("v"), Admin);

        var views = await Context.Services.GetRequiredService<IViewService>().GetViewsAsync(Org, project, discover: true);
        views.Select(view => view.Name).Should().Contain("pairs-only");
    }

    [Fact]
    public async Task Containers_HaveItemLevelTimeToLiveEnabled()
    {
        var client = Context.Services.GetRequiredService<ICosmosClientProvider>().Client;
        var database = client.GetDatabase(CosmosConstants.DatabaseName);
        var factory = Context.Services.GetRequiredService<IContainerFactory>();

        var created = (await database.GetContainer($"org.{Org}").ReadContainerAsync()).Resource;
        created.DefaultTimeToLive.Should().Be(-1, "containers of the platform get TTL on creation");

        var legacyName = $"legacy-{Guid.NewGuid():N}";
        await database.CreateContainerAsync(new ContainerProperties(legacyName, "/PartitionKey"));
        (await database.GetContainer(legacyName).ReadContainerAsync()).Resource.DefaultTimeToLive.Should().BeNull();

        await factory.EnsureContainerSettingsAsync(database.GetContainer(legacyName));
        (await database.GetContainer(legacyName).ReadContainerAsync()).Resource.DefaultTimeToLive.Should().Be(-1);

        await database.GetContainer(legacyName).DeleteContainerAsync();

        var project = NewProject("ttl");
        await KeyValues.SetAsync(Org, project, View, "active", Public("v"), Admin);
        (await ReadRawItemAsync(project, "kv.active")).Should().NotContain("\"ttl\"", "an active key has no ttl");
    }

    private CosmosKeyValueService CreateService(TimeSpan versionRetention, TimeSpan deletedRetention)
    {
        var services = Context.Services;
        var options = Options.Create(new KeyValueOptions
        {
            VersionRetention = versionRetention,
            DeletedRetention = deletedRetention,
            Protection = services.GetRequiredService<IOptions<KeyValueOptions>>().Value.Protection,
        });

        return new CosmosKeyValueService(
            services.GetRequiredService<IContainerRepositoryProvider>(),
            services.GetRequiredService<IValueProtector>(),
            services.GetRequiredService<KeyValues.Policy.IKeyValuePolicyStore>(),
            options,
            services.GetRequiredService<Json.IJsonSerializationSettingsProvider>(),
            services.GetRequiredService<IOptions<JsonSerializerSettings>>());
    }

    private async Task<string> ReadRawItemAsync(string project, string id)
    {
        var container = Context.Services.GetRequiredService<IContainerRepositoryProvider>().GetOrganizationContainer(Org).Container;
        using var response = await container.ReadItemStreamAsync(id, PartitionKeys.GetCosmosKeyValues(project, View));
        using var reader = new System.IO.StreamReader(response.Content, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;

        while (!await condition())
        {
            if (DateTimeOffset.UtcNow > deadline)
            {
                throw new TimeoutException("The condition was not met in time.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }
}
