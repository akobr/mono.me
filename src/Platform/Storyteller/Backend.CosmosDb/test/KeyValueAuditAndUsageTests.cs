#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.KeyValues.Audit;
using _42.Platform.Storyteller.KeyValues.Usage;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

public class KeyValueAuditAndUsageTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Org = TestConstants.Organization;
    private const string View = Constants.DefaultViewName;

    private static readonly KeyValueCaller Admin = new(AuditActor.Account("admin"), true, true, KeyValueChannel.User) { CorrelationId = "corr-1" };
    private static readonly KeyValueCaller Reader = KeyValueCaller.WithoutSecrets(AuditActor.Account("reader"));

    private IKeyValueService KeyValues => Context.Services.GetRequiredService<IKeyValueService>();

    private IKeyValueAuditLog Audit => Context.Services.GetRequiredService<IKeyValueAuditLog>();

    private IKeyValueUsageService Usage => Context.Services.GetRequiredService<IKeyValueUsageService>();

    [Fact]
    public async Task SecretOperations_AreAudited_PublicOnesAreNot()
    {
        var project = $"kv-audit-{Guid.NewGuid().ToString("N")[..12]}";
        await KeyValues.SetAsync(Org, project, View, "pub", new KeyValueWrite { Protection = KeyValueProtection.Public, Value = "p" }, Admin);
        await KeyValues.SetAsync(Org, project, View, "sec", new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "s1" }, Admin);
        await KeyValues.SetAsync(Org, project, View, "sec", new KeyValueWrite { Value = "s2" }, Admin);
        await KeyValues.GetAsync(Org, project, View, "sec", new KeyValueReadOptions { Reveal = true }, Admin);
        await KeyValues.GetAsync(Org, project, View, "sec", new KeyValueReadOptions(), Admin);
        await KeyValues.GetAsync(Org, project, View, "pub", new KeyValueReadOptions { Reveal = true }, Admin);
        await KeyValues.ExportAsync(Org, project, View, new KeyValueExportRequest { IncludeSecrets = true }, Admin);
        await KeyValues.DeleteAsync(Org, project, View, "sec", new KeyValueDeleteOptions(), Admin);
        await KeyValues.RestoreAsync(Org, project, View, new KeyValueBulkRequest { Keys = ["sec"] }, Admin);
        await KeyValues.DeleteAsync(Org, project, View, "pub", new KeyValueDeleteOptions(), Admin);
        await KeyValues.SetAsync(Org, project, View, "pub", new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "now secret" }, Admin);

        var all = await Audit.QueryAsync(Org, project, new KeyValueAuditQuery());

        all.Items.Select(entry => entry.Operation).Should().BeEquivalentTo(
        [
            KeyValueAuditOperation.Create,
            KeyValueAuditOperation.Update,
            KeyValueAuditOperation.Reveal,
            KeyValueAuditOperation.Export,
            KeyValueAuditOperation.Delete,
            KeyValueAuditOperation.Restore,
            KeyValueAuditOperation.Update,
        ]);
        all.Items.Should().BeInDescendingOrder(entry => entry.Time);
        all.Items.Should().OnlyContain(entry => entry.Actor == AuditActor.Account("admin") && entry.CorrelationId == "corr-1" && entry.Channel == KeyValueChannel.User);
        all.Items.Single(entry => entry.Operation == KeyValueAuditOperation.Update && entry.Key == "pub").Detail.Should().Be("Public -> Standard");
        all.Items.Single(entry => entry.Operation == KeyValueAuditOperation.Export).Detail.Should().Be("1 secret value(s)");

        var reveals = await Audit.QueryAsync(Org, project, new KeyValueAuditQuery { Operation = KeyValueAuditOperation.Reveal, Key = "sec" });
        reveals.Items.Should().ContainSingle();
        reveals.Items[0].Version.Should().Be(2);
        reveals.Items[0].Protection.Should().Be(KeyValueProtection.Standard);

        var future = await Audit.QueryAsync(Org, project, new KeyValueAuditQuery { From = DateTimeOffset.UtcNow.AddMinutes(5) });
        future.Items.Should().BeEmpty();
    }

    // A secret version read with reveal is a reveal on both paths: the current version from the catalog item, an older one
    // from its history item.
    [Fact]
    public async Task SecretVersionReads_AreAuditedAsReveals()
    {
        var project = $"kv-audit-ver-{Guid.NewGuid().ToString("N")[..8]}";
        await KeyValues.SetAsync(Org, project, View, "sec", new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "s1" }, Admin);
        await KeyValues.SetAsync(Org, project, View, "sec", new KeyValueWrite { Value = "s2" }, Admin);
        await KeyValues.SetAsync(Org, project, View, "sec", new KeyValueWrite { Value = "s3" }, Admin);
        await KeyValues.SetAsync(Org, project, View, "pub", new KeyValueWrite { Protection = KeyValueProtection.Public, Value = "p1" }, Admin);
        await KeyValues.SetAsync(Org, project, View, "pub", new KeyValueWrite { Value = "p2" }, Admin);

        (await KeyValues.GetVersionAsync(Org, project, View, "sec", 3, reveal: true, Admin))!.Value.Should().Be("s3");
        (await KeyValues.GetVersionAsync(Org, project, View, "sec", 1, reveal: true, Admin))!.Value.Should().Be("s1");
        (await KeyValues.GetVersionAsync(Org, project, View, "sec", 3, reveal: false, Admin))!.Value.Should().BeNull();
        (await KeyValues.GetVersionAsync(Org, project, View, "sec", 2, reveal: false, Admin))!.Value.Should().BeNull();
        (await KeyValues.GetVersionAsync(Org, project, View, "pub", 2, reveal: true, Admin))!.Value.Should().Be("p2");
        (await KeyValues.GetVersionAsync(Org, project, View, "pub", 1, reveal: true, Admin))!.Value.Should().Be("p1");
        (await KeyValues.GetVersionAsync(Org, project, View, "sec", 9, reveal: true, Admin)).Should().BeNull();
        var withoutRights = () => KeyValues.GetVersionAsync(Org, project, View, "sec", 2, reveal: true, Reader);
        await withoutRights.Should().ThrowAsync<AccessDeniedException>();

        var reveals = await Audit.QueryAsync(Org, project, new KeyValueAuditQuery { Operation = KeyValueAuditOperation.Reveal });

        reveals.Items.Should().HaveCount(2, "only the two secret versions read with reveal and secret rights are reveals");
        reveals.Items.Select(entry => (entry.Key, entry.Version, entry.Detail)).Should().BeEquivalentTo(
        [
            ("sec", (ulong?)3, "version 3"),
            ("sec", (ulong?)1, "version 1"),
        ]);
        reveals.Items.Should().OnlyContain(entry => entry.Protection == KeyValueProtection.Standard && entry.View == View);
        reveals.Items.Should().OnlyContain(entry => entry.Actor == AuditActor.Account("admin") && entry.CorrelationId == "corr-1" && entry.Channel == KeyValueChannel.User);
    }

    // A diff of a secret carries the plaintext of both sides: each secret version it resolves is one reveal, written only
    // after the whole diff resolved.
    [Fact]
    public async Task SecretDiffs_AreAuditedPerSecretVersion()
    {
        var project = $"kv-audit-diff-{Guid.NewGuid().ToString("N")[..8]}";
        await KeyValues.SetAsync(Org, project, View, "Db.Password", new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "s1" }, Admin);
        await KeyValues.SetAsync(Org, project, View, "Db.Password", new KeyValueWrite { Value = "s2" }, Admin);
        await KeyValues.SetAsync(Org, project, View, "pub", new KeyValueWrite { Protection = KeyValueProtection.Public, Value = "p1" }, Admin);
        await KeyValues.SetAsync(Org, project, View, "pub", new KeyValueWrite { Value = "p2" }, Admin);
        await KeyValues.SetAsync(Org, project, View, "moved", new KeyValueWrite { Protection = KeyValueProtection.Public, Value = "m1" }, Admin);
        await KeyValues.SetAsync(Org, project, View, "moved", new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "m2" }, Admin);

        async Task<KeyValueAuditEvent[]> RevealsAfter(Func<Task> act)
        {
            var before = (await Audit.QueryAsync(Org, project, new KeyValueAuditQuery { Operation = KeyValueAuditOperation.Reveal })).Items.Select(entry => entry.Id).ToHashSet();
            await act();
            var after = await Audit.QueryAsync(Org, project, new KeyValueAuditQuery { Operation = KeyValueAuditOperation.Reveal });
            return after.Items.Where(entry => !before.Contains(entry.Id)).ToArray();
        }

        var bothSides = await RevealsAfter(() => KeyValues.GetVersionChangesAsync(Org, project, View, "db.password", 1, 2, Admin));
        bothSides.Select(entry => (entry.Key, entry.Version, entry.Detail)).Should().BeEquivalentTo(
            [("Db.Password", (ulong?)1, "diff 1..2"), ("Db.Password", (ulong?)2, "diff 1..2")],
            "the stored key is recorded, not the casing of the request");
        bothSides.Should().OnlyContain(entry => entry.Protection == KeyValueProtection.Standard
            && entry.Actor == AuditActor.Account("admin") && entry.CorrelationId == "corr-1" && entry.Channel == KeyValueChannel.User);

        var fromNothing = await RevealsAfter(() => KeyValues.GetVersionChangesAsync(Org, project, View, "Db.Password", 0, 1, Admin));
        fromNothing.Select(entry => (entry.Version, entry.Detail)).Should().BeEquivalentTo([((ulong?)1, "diff 0..1")], "version 0 is the empty text");

        var sameVersion = await RevealsAfter(() => KeyValues.GetVersionChangesAsync(Org, project, View, "Db.Password", 2, 2, Admin));
        sameVersion.Select(entry => (entry.Version, entry.Detail)).Should().BeEquivalentTo([((ulong?)2, "diff 2..2")], "one version compared with itself is one reveal");

        var levelChange = await RevealsAfter(() => KeyValues.GetVersionChangesAsync(Org, project, View, "moved", 1, 2, Admin));
        levelChange.Select(entry => (entry.Key, entry.Version, entry.Protection)).Should().BeEquivalentTo(
            [("moved", (ulong?)2, (KeyValueProtection?)KeyValueProtection.Standard)],
            "only the secret side of a level change is a reveal");

        (await RevealsAfter(() => KeyValues.GetVersionChangesAsync(Org, project, View, "pub", 1, 2, Admin))).Should().BeEmpty();

        var unknown = await RevealsAfter(async () =>
        {
            var act = () => KeyValues.GetVersionChangesAsync(Org, project, View, "Db.Password", 1, 9, Admin);
            await act.Should().ThrowAsync<InvalidOperationException>();
        });
        unknown.Should().BeEmpty("a diff that fails on its second side reveals nothing");

        var withoutRights = await RevealsAfter(async () =>
        {
            var act = () => KeyValues.GetVersionChangesAsync(Org, project, View, "Db.Password", 1, 2, Reader);
            await act.Should().ThrowAsync<AccessDeniedException>();
        });
        withoutRights.Should().BeEmpty();
    }

    [Fact]
    public async Task Usage_CountsRevealsOfSecretVersionsAndDiffs()
    {
        var project = $"kv-usage-ver-{Guid.NewGuid().ToString("N")[..8]}";
        await KeyValues.SetAsync(Org, project, View, "s", new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "1" }, Admin);
        await KeyValues.SetAsync(Org, project, View, "s", new KeyValueWrite { Value = "2" }, Admin);
        await KeyValues.SetAsync(Org, project, View, "p", new KeyValueWrite { Protection = KeyValueProtection.Public, Value = "1" }, Admin);
        await KeyValues.GetVersionAsync(Org, project, View, "s", 2, reveal: true, Admin);
        await KeyValues.GetVersionAsync(Org, project, View, "s", 1, reveal: true, Admin);
        await KeyValues.GetVersionAsync(Org, project, View, "s", 1, reveal: false, Admin);
        await KeyValues.GetVersionAsync(Org, project, View, "p", 1, reveal: true, Admin);
        await KeyValues.GetVersionChangesAsync(Org, project, View, "s", 1, 2, Admin);
        await KeyValues.GetVersionChangesAsync(Org, project, View, "s", 0, 1, Admin);
        await KeyValues.GetVersionChangesAsync(Org, project, View, "p", 0, 1, Admin);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await Usage.FlushAsync();
        await Usage.TakeSnapshotAsync(today);
        var report = await Usage.GetReportAsync(Org, project, today, today);

        report.Days.Should().ContainSingle();
        report.Days[0].Values[KeyValueUsageMeters.Reveals].Should().Be(5, "two secret version reads, two sides of the first diff and one of the second");
        var reveals = await Audit.QueryAsync(Org, project, new KeyValueAuditQuery { Operation = KeyValueAuditOperation.Reveal });
        reveals.Items.Should().HaveCount(5, "one event per counted reveal");
    }

    [Fact]
    public async Task BindingReads_AreAggregatedPerActorKeyAndHour()
    {
        var project = $"kv-bind-audit-{Guid.NewGuid().ToString("N")[..8]}";
        await KeyValues.SetAsync(Org, project, View, "s", new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "v" }, Admin);
        await KeyValues.SetAsync(Org, project, View, "p", new KeyValueWrite { Protection = KeyValueProtection.Public, Value = "v" }, Admin);
        var machine = new KeyValueCaller(AuditActor.Machine("m1"), true, true, KeyValueChannel.MachineMtls);

        KeyValueCallerContext.Current = machine;

        try
        {
            await KeyValues.LookupAsync(Org, project, View, "s", includeSecrets: true);
            await KeyValues.LookupAsync(Org, project, View, "s", includeSecrets: true);
            await KeyValues.LookupAsync(Org, project, View, "S", includeSecrets: true);
            await KeyValues.LookupAsync(Org, project, View, "p", includeSecrets: true);
            await KeyValues.LookupAsync(Org, project, View, "s", includeSecrets: false);
        }
        finally
        {
            KeyValueCallerContext.Current = null;
        }

        await Usage.FlushAsync();

        var reads = await Audit.QueryAsync(Org, project, new KeyValueAuditQuery { Operation = KeyValueAuditOperation.BindingRead });
        reads.Items.Should().ContainSingle("three reads of one secret by one actor make one event; public reads are not audited");
        reads.Items[0].Count.Should().Be(3);
        reads.Items[0].Key.Should().Be("s");
        reads.Items[0].Actor.Should().Be(AuditActor.Machine("m1"));
        reads.Items[0].Channel.Should().Be(KeyValueChannel.MachineMtls);
    }

    // A reveal through an actor (a Storyteller user token of the MCP server) is audited as the account via the actor.
    [Fact]
    public async Task SecretOperationsThroughAnActor_AreAuditedWithTheActor()
    {
        var project = $"kv-audit-via-{Guid.NewGuid().ToString("N")[..8]}";
        var throughMcp = new KeyValueCaller(AuditActor.Account("admin", "Ada") with { Via = "mcp" }, true, true, KeyValueChannel.User) { CorrelationId = "corr-2" };
        await KeyValues.SetAsync(Org, project, View, "sec", new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "s1" }, Admin);
        await KeyValues.GetAsync(Org, project, View, "sec", new KeyValueReadOptions { Reveal = true }, throughMcp);

        var all = await Audit.QueryAsync(Org, project, new KeyValueAuditQuery());

        all.Items.Single(entry => entry.Operation == KeyValueAuditOperation.Reveal).Actor.Should().Be(throughMcp.Actor);
        var created = all.Items.Single(entry => entry.Operation == KeyValueAuditOperation.Create);
        created.Actor.Should().Be(AuditActor.Account("admin"));
        created.Actor.Via.Should().BeNull();
    }

    // The actor is part of the aggregation key: binding reads through it count apart from the direct ones of the account.
    [Fact]
    public async Task BindingReadsThroughAnActor_AreAggregatedApartFromDirectOnes()
    {
        var project = $"kv-bind-via-{Guid.NewGuid().ToString("N")[..8]}";
        await KeyValues.SetAsync(Org, project, View, "s", new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "v" }, Admin);
        var direct = new KeyValueCaller(AuditActor.Account("ada", "Ada"), true, true, KeyValueChannel.User);
        var throughMcp = direct with { Actor = direct.Actor with { Via = "mcp" } };

        try
        {
            KeyValueCallerContext.Current = direct;
            await KeyValues.LookupAsync(Org, project, View, "s", includeSecrets: true);
            await KeyValues.LookupAsync(Org, project, View, "s", includeSecrets: true);
            KeyValueCallerContext.Current = throughMcp;
            await KeyValues.LookupAsync(Org, project, View, "s", includeSecrets: true);
        }
        finally
        {
            KeyValueCallerContext.Current = null;
        }

        await Usage.FlushAsync();

        var reads = await Audit.QueryAsync(Org, project, new KeyValueAuditQuery { Operation = KeyValueAuditOperation.BindingRead });
        reads.Items.Should().HaveCount(2);
        reads.Items.Single(entry => entry.Actor.Via == "mcp").Count.Should().Be(1);
        reads.Items.Single(entry => entry.Actor.Via is null).Count.Should().Be(2);
        reads.Items.Should().OnlyContain(entry => entry.Actor.Id == "ada" && entry.Actor.Name == "Ada");
    }

    [Fact]
    public async Task Usage_CountersFlushAndTheSnapshotCountsKeys()
    {
        var project = $"kv-usage-{Guid.NewGuid().ToString("N")[..12]}";
        await KeyValues.SetAsync(Org, project, View, "a", new KeyValueWrite { Protection = KeyValueProtection.Public, Value = "1" }, Admin);
        await KeyValues.SetAsync(Org, project, View, "a", new KeyValueWrite { Value = "2" }, Admin);
        await KeyValues.SetAsync(Org, project, View, "ref", new KeyValueWrite { Protection = KeyValueProtection.Public, Value = "kv://vault/name", ContentType = KeyValueContentTypes.SecretReference }, Admin);
        await KeyValues.SetAsync(Org, project, View, "s", new KeyValueWrite { Protection = KeyValueProtection.Standard, Value = "x" }, Admin);
        await KeyValues.SetAsync(Org, project, View, "gone", new KeyValueWrite { Protection = KeyValueProtection.Public, Value = "x" }, Admin);
        await KeyValues.DeleteAsync(Org, project, View, "gone", new KeyValueDeleteOptions(), Admin);
        await KeyValues.GetAsync(Org, project, View, "a", new KeyValueReadOptions(), Admin);
        await KeyValues.GetAsync(Org, project, View, "s", new KeyValueReadOptions { Reveal = true }, Admin);
        await KeyValues.LookupAsync(Org, project, View, "a", includeSecrets: false);
        await KeyValues.ListAsync(Org, project, View, new KeyValueListRequest());

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await Usage.FlushAsync();
        await Usage.TakeSnapshotAsync(today);
        var report = await Usage.GetReportAsync(Org, project, today, today);

        report.Days.Should().ContainSingle();
        var day = report.Days[0].Values;
        day[KeyValueUsageMeters.Writes].Should().Be(6, "4 creates, 1 update, 1 delete");
        day[KeyValueUsageMeters.ReadsApi].Should().Be(3, "two gets and one list");
        day[KeyValueUsageMeters.ReadsBinding].Should().Be(1);
        day[KeyValueUsageMeters.Reveals].Should().Be(1);
        day[KeyValueUsageMeters.KmsStandard].Should().BeGreaterThanOrEqualTo(2, "one wrap and one unwrap at least");
        day[KeyValueUsageMeters.KeysPublic].Should().Be(2, "a and ref; the deleted key is not counted");
        day[KeyValueUsageMeters.KeysReference].Should().Be(1);
        day[KeyValueUsageMeters.KeysStandard].Should().Be(1);
        day[KeyValueUsageMeters.Versions].Should().Be(2, "the old version of a, and the history of the deleted key");
        day[KeyValueUsageMeters.StorageBytes].Should().BeGreaterThan(0);
        report.Totals[KeyValueUsageMeters.KeysPublic].Should().Be(2, "one day: the average equals the count");
        report.EstimatedCharges.Should().BeNull("no pricing is configured in the tests");

        var empty = await Usage.GetReportAsync(Org, project, today.AddDays(-10), today.AddDays(-5));
        empty.Days.Should().BeEmpty();
        empty.Totals.Should().BeEmpty();

        var tooLong = () => Usage.GetReportAsync(Org, project, today.AddDays(-500), today);
        await tooLong.Should().ThrowAsync<InvalidInputException>();
    }
}
