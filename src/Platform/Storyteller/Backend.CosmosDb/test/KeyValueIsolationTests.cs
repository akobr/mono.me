#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Entities.Access;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.KeyValues.Audit;
using _42.Platform.Storyteller.KeyValues.Policy;
using _42.Platform.Storyteller.KeyValues.Usage;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Tenant isolation of key/value pairs in the shared container "organizations": two organizations with the same project,
// views and keys, whose values carry the organization ("a-..." and "b-..."), so a pair of the other one is visible
// wherever it leaks. Every read through one organization shows only its own pairs, and nothing done through A (writes,
// deletes, restores, purges, imports, the rotation and re-wrap) changes an item of B.
public class KeyValueIsolationTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Project = "billing";
    private const string View = Constants.DefaultViewName;
    private const string Prod = "prod";

    // B writes one more version of db.host than A: a version that exists only in B is a leak when A shows it.
    private const int HostVersionsA = 2;
    private const int HostVersionsB = 3;

    private static readonly KeyValueCaller AdminA = new(AuditActor.Account("admin-a"), true, true, KeyValueChannel.User) { CorrelationId = "corr-a" };
    private static readonly KeyValueCaller AdminB = new(AuditActor.Account("admin-b"), true, true, KeyValueChannel.User) { CorrelationId = "corr-b" };

    // xUnit creates the class for every test: each test gets two organizations of its own, apart from the other tests and
    // from earlier runs on a reused emulator.
    private readonly string _suffix = Guid.NewGuid().ToString("N")[..8];

    private string OrgA => $"iso-kv-a-{_suffix}";

    private string OrgB => $"iso-kv-b-{_suffix}";

    private IKeyValueService KeyValues => Context.Services.GetRequiredService<IKeyValueService>();

    private IKeyValuePolicyService Policies => Context.Services.GetRequiredService<IKeyValuePolicyService>();

    private IKeyValueMaintenanceService Maintenance => Context.Services.GetRequiredService<IKeyValueMaintenanceService>();

    private IKeyValueAuditLog Audit => Context.Services.GetRequiredService<IKeyValueAuditLog>();

    private IKeyValueUsageService Usage => Context.Services.GetRequiredService<IKeyValueUsageService>();

    private IViewService Views => Context.Services.GetRequiredService<IViewService>();

    private IContainerRepositoryProvider Repositories => Context.Services.GetRequiredService<IContainerRepositoryProvider>();

    private FakeKeyEncryptionProvider Standard => Context.Services.GetRequiredService<FakeKeyEncryptionProvider>();

    [Fact]
    public async Task Reads_ThroughEachOrganization_SeeOnlyItsOwnPairs()
    {
        await SeedBothAsync();

        // the two organizations store the same partition key values and ids side by side in the shared container
        var samePlaces = new[]
        {
            (PartitionKeys.GetKeyValues(Project, View), "kv.db.host"),
            (PartitionKeys.GetKeyValues(Project, View), "kv.db.password"),
            (PartitionKeys.GetKeyValues(Project, Prod), "kv.db.password"),
        };
        (await ReadItemsAsync(OrgA)).Select(item => (item.PartitionKey, item.Id)).Should().Contain(samePlaces);
        (await ReadItemsAsync(OrgB)).Select(item => (item.PartitionKey, item.Id)).Should().Contain(samePlaces);

        await AssertSeesOnlyItsOwnPairsAsync(OrgA, "a", "b", HostVersionsA, AdminA);
        await AssertSeesOnlyItsOwnPairsAsync(OrgB, "b", "a", HostVersionsB, AdminB);
    }

    [Fact]
    public async Task WritesDeletesRestoresAndPurges_InA_LeaveBUnchanged()
    {
        await SeedBothAsync();

        // the description reveals B's secrets, which audits them in B: it goes before the snapshot of B's items
        var bDescribed = await DescribeAsync(OrgB, "b", AdminB);
        var bItems = await ReadItemsAsync(OrgB);

        (await KeyValues.SetAsync(OrgA, Project, View, "db.host", Public("a-host-3"), AdminA)).Version.Should().Be(3);
        (await KeyValues.SetAsync(OrgA, Project, View, "new.key", Public("a-new"), AdminA)).Version.Should().Be(1);

        // B's old.key is deleted too: the restore through A restores A's only
        var restored = await KeyValues.RestoreAsync(OrgA, Project, View, new KeyValueBulkRequest { KeyPrefix = "old." }, AdminA);
        restored.Items.Select(item => (item.Key, item.Outcome)).Should().Equal(("old.key", KeyValueBulkOutcome.Done));

        (await KeyValues.DeleteAsync(OrgA, Project, View, "app.name", new KeyValueDeleteOptions(), AdminA)).Should().BeTrue();
        var purged = await KeyValues.PurgeAsync(OrgA, Project, View, new KeyValueBulkRequest { KeyPrefix = "app." }, AdminA);
        purged.Items.Select(item => (item.Key, item.Outcome)).Should().Equal(("app.name", KeyValueBulkOutcome.Done));

        // A's old.key is active again; a purge through A must not find B's deleted one
        var notDeleted = await KeyValues.PurgeAsync(OrgA, Project, View, new KeyValueBulkRequest { Keys = ["old.key"] }, AdminA);
        notDeleted.Items.Should().ContainSingle().Which.Outcome.Should().Be(KeyValueBulkOutcome.NotFound);

        (await KeyValues.DeleteAsync(OrgA, Project, View, "db.password", new KeyValueDeleteOptions { Permanent = true }, AdminA)).Should().BeTrue();
        (await KeyValues.DeleteAsync(OrgA, Project, Prod, "db.host", new KeyValueDeleteOptions(), AdminA)).Should().BeTrue();
        (await KeyValues.PurgeAsync(OrgA, Project, Prod, new KeyValueBulkRequest { Keys = ["db.host"] }, AdminA)).Items
            .Should().ContainSingle().Which.Outcome.Should().Be(KeyValueBulkOutcome.Done);
        (await KeyValues.SetAsync(OrgA, Project, Prod, "db.password", Secret("a-prod-secret-2"), AdminA)).Version.Should().Be(2);

        var aActive = await KeyValues.ListAsync(OrgA, Project, View, new KeyValueListRequest { IncludeValues = true });
        aActive.Items.Select(item => (item.Key, item.Value)).Should().Equal(
            ("db.host", "a-host-3"),
            ("new.key", "a-new"),
            ("old.key", "a-old"),
            ("only.a", "a-only"));
        (await KeyValues.ListAsync(OrgA, Project, View, new KeyValueListRequest { State = KeyValueState.Deleted })).Items.Should().BeEmpty();
        (await KeyValues.GetVersionsAsync(OrgA, Project, View, "db.password")).Should().BeEmpty();
        (await KeyValues.ListAsync(OrgA, Project, Prod, new KeyValueListRequest())).Items.Select(item => item.Key).Should().Equal("db.password");

        (await ReadItemsAsync(OrgB)).Should().BeEquivalentTo(bItems, "nothing done through A creates, replaces, patches or deletes an item of B");
        (await DescribeAsync(OrgB, "b", AdminB)).Should().Equal(bDescribed);

        var bOld = await KeyValues.GetAsync(OrgB, Project, View, "old.key", new KeyValueReadOptions { IncludeDeleted = true }, AdminB);
        bOld!.IsDeleted.Should().BeTrue("restoring the name in A restores nothing in B");
        bOld.Value.Should().Be("b-old");
        (await KeyValues.GetAsync(OrgB, Project, View, "app.name", new KeyValueReadOptions(), AdminB))!.Value.Should().Be("b-app");
        (await KeyValues.GetAsync(OrgB, Project, View, "db.password", new KeyValueReadOptions { Reveal = true }, AdminB))!.Value.Should().Be("b-secret-2");
        (await KeyValues.GetVersionsAsync(OrgB, Project, View, "db.password")).Select(version => version.Version).Should().Equal(1u, 2u);
        (await KeyValues.GetAsync(OrgB, Project, Prod, "db.host", new KeyValueReadOptions(), AdminB))!.Value.Should().Be("b-prod-host");
    }

    [Fact]
    public async Task ExportAndReplacingImport_InA_LeaveBUnchanged()
    {
        await SeedBothAsync();
        var bDescribed = await DescribeAsync(OrgB, "b", AdminB);
        var bItems = await ReadItemsAsync(OrgB);

        var exported = await KeyValues.ExportAsync(OrgA, Project, View, new KeyValueExportRequest { IncludeSecrets = true }, AdminA);
        exported.Items.Select(item => (item.Key, item.Value)).Should().Equal(
            ("app.name", "a-app"),
            ("db.host", "a-host-2"),
            ("db.password", "a-secret-2"),
            ("only.a", "a-only"));

        // Replace soft-deletes every active key of A's view the document does not list; B has the same keys in the same view
        var document = new KeyValueDocument
        {
            Items =
            [
                new() { Key = "db.host", Protection = KeyValueProtection.Public, Value = "a-host-3" },
                new() { Key = "fresh.one", Protection = KeyValueProtection.Public, Value = "a-fresh" },
            ],
        };
        var replace = new KeyValueImportOptions { Mode = KeyValueImportMode.Replace };
        (string Key, KeyValueImportAction Action)[] plan =
        [
            ("db.host", KeyValueImportAction.Update),
            ("fresh.one", KeyValueImportAction.Create),
            ("app.name", KeyValueImportAction.Delete),
            ("db.password", KeyValueImportAction.Delete),
            ("only.a", KeyValueImportAction.Delete),
        ];

        var dryRun = await KeyValues.ImportAsync(OrgA, Project, View, document, replace with { DryRun = true }, AdminA);
        dryRun.Items.Select(item => (item.Key, item.Action)).Should().Equal(plan);

        var imported = await KeyValues.ImportAsync(OrgA, Project, View, document, replace, AdminA);
        imported.IsComplete.Should().BeTrue();
        imported.Items.Select(item => (item.Key, item.Action)).Should().Equal(plan);

        var emptied = await KeyValues.ImportAsync(OrgA, Project, Prod, new KeyValueDocument { Items = [] }, replace, AdminA);
        emptied.Items.Select(item => (item.Key, item.Action)).Should().Equal(
            ("db.host", KeyValueImportAction.Delete),
            ("db.password", KeyValueImportAction.Delete));

        var aActive = await KeyValues.ListAsync(OrgA, Project, View, new KeyValueListRequest { IncludeValues = true });
        aActive.Items.Select(item => (item.Key, item.Value)).Should().Equal(("db.host", "a-host-3"), ("fresh.one", "a-fresh"));
        (await KeyValues.ListAsync(OrgA, Project, Prod, new KeyValueListRequest())).Items.Should().BeEmpty();

        (await ReadItemsAsync(OrgB)).Should().BeEquivalentTo(bItems, "an import into A deletes and writes nothing of B");
        (await DescribeAsync(OrgB, "b", AdminB)).Should().Equal(bDescribed);

        var bExported = await KeyValues.ExportAsync(OrgB, Project, View, new KeyValueExportRequest { IncludeSecrets = true }, AdminB);
        bExported.Items.Select(item => (item.Key, item.Value)).Should().Equal(
            ("app.name", "b-app"),
            ("db.host", "b-host-3"),
            ("db.password", "b-secret-2"),
            ("only.b", "b-only"));
    }

    // CountInUseAsync is an organization-wide query over every key/value partition of the project.
    [Fact]
    public async Task CountInUse_CountsTheKeysOfTheOrganizationOnly()
    {
        await CreateAccessPointsAsync();
        await SeedBothAsync();
        await KeyValues.SetAsync(OrgB, Project, View, "extra.public", Public("b-extra"), AdminB);
        await KeyValues.SetAsync(OrgB, Project, View, "extra.secret", Secret("b-extra-secret"), AdminB);

        var allowHsm = new KeyValuePolicyUpdate { AllowedProtections = [KeyValueProtection.Public, KeyValueProtection.Standard, KeyValueProtection.Hsm] };
        await Policies.UpdateAsync(OrgA, Project, allowHsm, AdminA);
        await Policies.UpdateAsync(OrgB, Project, allowHsm, AdminB);
        await KeyValues.SetAsync(OrgB, Project, View, "hsm.secret", new KeyValueWrite { Protection = KeyValueProtection.Hsm, Value = "b-hsm" }, AdminB);

        var service = Policies.Should().BeOfType<CosmosKeyValuePolicyService>().Which;

        // active and deleted keys: old.key is deleted in both
        Counts(await service.CountInUseAsync(OrgA, Project, KeyValueProtection.Public)).Should().Equal("default=4", "pairs-a=1", "prod=1");
        Counts(await service.CountInUseAsync(OrgB, Project, KeyValueProtection.Public)).Should().Equal("default=5", "pairs-b=1", "prod=1");
        Counts(await service.CountInUseAsync(OrgA, Project, KeyValueProtection.Standard)).Should().Equal("default=1", "prod=1");
        Counts(await service.CountInUseAsync(OrgB, Project, KeyValueProtection.Standard)).Should().Equal("default=2", "prod=1");
        Counts(await service.CountInUseAsync(OrgA, Project, KeyValueProtection.Hsm)).Should().BeEmpty("only B has an HSM secret");
        Counts(await service.CountInUseAsync(OrgB, Project, KeyValueProtection.Hsm)).Should().Equal("default=1");

        // the count decides whether a level can be disabled: A's HSM level goes, B's is still in use
        var disableHsm = new KeyValuePolicyUpdate { AllowedProtections = [KeyValueProtection.Public, KeyValueProtection.Standard] };
        (await Policies.UpdateAsync(OrgA, Project, disableHsm, AdminA)).AllowedProtections.Should().NotContain(KeyValueProtection.Hsm);
        var disableInB = () => Policies.UpdateAsync(OrgB, Project, disableHsm, AdminB);
        (await disableInB.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.ProtectionInUse);
    }

    // The re-wrap queries every key/value partition of the project across the organization. The fake key provider is one
    // for every organization, so after A's rotation B's envelopes are on an old key version too: only the predicate on
    // the organization keeps them out of A's run.
    [Fact]
    public async Task Rewrap_OfA_MovesOnlyAsEnvelopes()
    {
        await CreateAccessPointsAsync();
        await SeedBothAsync();
        var bItems = await ReadItemsAsync(OrgB);
        var bPolicy = await Policies.GetAsync(OrgB, Project);
        var unwrapsBefore = Standard.UnwrapCount;

        var rotated = await Policies.RotateAsync(OrgA, Project, KeyValueProtection.Standard, AdminA);
        var status = await Maintenance.RewrapAsync(OrgA, Project, KeyValueProtection.Standard);

        // A's envelopes: db.password (the current item and the history of version 1) and db.password of prod
        status!.Processed.Should().Be(3, "only the envelopes of A are re-wrapped");
        status.LastError.Should().BeNull();
        status.CompletedAt.Should().NotBeNull();
        status.TargetKeyVersion.Should().Be(rotated.Rewrap!.TargetKeyVersion);
        (Standard.UnwrapCount - unwrapsBefore).Should().Be(3, "the re-wrap of A never unwraps an envelope of B");

        var currentSuffix = $"/{Standard.CurrentVersion}";
        (await ReadEnvelopeKeyIdsAsync(OrgA)).Should().HaveCount(3).And.OnlyContain(keyId => keyId.EndsWith(currentSuffix, StringComparison.Ordinal));
        (await ReadEnvelopeKeyIdsAsync(OrgB)).Should().HaveCount(3).And.NotContain(keyId => keyId.EndsWith(currentSuffix, StringComparison.Ordinal));
        (await ReadItemsAsync(OrgB)).Should().BeEquivalentTo(bItems, "the rotation and the re-wrap of A patch no item of B");

        var bPolicyAfter = await Policies.GetAsync(OrgB, Project);
        bPolicyAfter.Version.Should().Be(bPolicy.Version);
        bPolicyAfter.Rewrap.Should().BeNull("the rotation of A starts no re-wrap in B");
        (await Audit.QueryAsync(OrgB, Project, new KeyValueAuditQuery { Operation = KeyValueAuditOperation.Rewrap })).Items.Should().BeEmpty();

        // B's own run still finds all of its envelopes on the old key version
        var bStatus = await Maintenance.RewrapAsync(OrgB, Project, KeyValueProtection.Standard);
        bStatus!.Processed.Should().Be(3);
        (await ReadEnvelopeKeyIdsAsync(OrgB)).Should().OnlyContain(keyId => keyId.EndsWith(currentSuffix, StringComparison.Ordinal));

        (await KeyValues.GetAsync(OrgA, Project, View, "db.password", new KeyValueReadOptions { Reveal = true }, AdminA))!.Value.Should().Be("a-secret-2");
        (await KeyValues.GetVersionAsync(OrgA, Project, View, "db.password", 1, reveal: true, AdminA))!.Value.Should().Be("a-secret-1");
        (await KeyValues.GetAsync(OrgB, Project, View, "db.password", new KeyValueReadOptions { Reveal = true }, AdminB))!.Value.Should().Be("b-secret-2");
        (await KeyValues.GetAsync(OrgB, Project, Prod, "db.password", new KeyValueReadOptions { Reveal = true }, AdminB))!.Value.Should().Be("b-prod-secret");
    }

    // The snapshot's gauges come from organization-wide GROUP BY queries, one organization after another.
    [Fact]
    public async Task UsageSnapshot_CountsEachOrganizationApart()
    {
        // the snapshot finds the organizations by their access points in core
        await CreateAccessPointsAsync();
        await SeedBothAsync();
        await KeyValues.SetAsync(OrgA, Project, View, "extra.secret", Secret("a-extra"), AdminA);
        await KeyValues.SetAsync(OrgB, Project, View, "extra.public", Public("b-extra"), AdminB);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        await Usage.FlushAsync();
        await Usage.TakeSnapshotAsync(today);

        var dayA = (await Usage.GetReportAsync(OrgA, Project, today, today)).Days.Should().ContainSingle().Which.Values;
        dayA[KeyValueUsageMeters.KeysPublic].Should().Be(5, "db.host, app.name and only.a, db.host of prod and k of pairs-a");
        dayA[KeyValueUsageMeters.KeysStandard].Should().Be(3, "db.password, db.password of prod and extra.secret");
        dayA[KeyValueUsageMeters.Versions].Should().Be(3, "version 1 of db.host and of db.password, and the history of the deleted old.key");
        dayA[KeyValueUsageMeters.Writes].Should().Be(12, "11 writes of the seed and extra.secret");
        dayA[KeyValueUsageMeters.StorageBytes].Should().BeGreaterThan(0);

        var dayB = (await Usage.GetReportAsync(OrgB, Project, today, today)).Days.Should().ContainSingle().Which.Values;
        dayB[KeyValueUsageMeters.KeysPublic].Should().Be(6, "the keys A has, and extra.public");
        dayB[KeyValueUsageMeters.KeysStandard].Should().Be(2);
        dayB[KeyValueUsageMeters.Versions].Should().Be(4, "versions 1 and 2 of db.host, version 1 of db.password and the deleted old.key");
        dayB[KeyValueUsageMeters.Writes].Should().Be(13, "12 writes of the seed and extra.public");
        dayB[KeyValueUsageMeters.StorageBytes].Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AuditQuery_ReturnsTheEventsOfTheOrganizationOnly()
    {
        await SeedBothAsync();
        var reveal = new KeyValueReadOptions { Reveal = true };
        (await KeyValues.GetAsync(OrgA, Project, View, "db.password", reveal, AdminA))!.Value.Should().Be("a-secret-2");
        (await KeyValues.GetAsync(OrgB, Project, View, "db.password", reveal, AdminB))!.Value.Should().Be("b-secret-2");
        (await KeyValues.GetAsync(OrgB, Project, View, "db.password", reveal, AdminB))!.Value.Should().Be("b-secret-2");
        (await KeyValues.GetVersionAsync(OrgB, Project, View, "db.password", 1, reveal: true, AdminB))!.Value.Should().Be("b-secret-1");

        // binding reads of the same key by the same actor are aggregated, per organization
        try
        {
            KeyValueCallerContext.Current = AdminA;

            for (var i = 0; i < 2; i++)
            {
                (await KeyValues.LookupAsync(OrgA, Project, View, "db.password", includeSecrets: true))!.Value.Should().Be("a-secret-2");
            }

            KeyValueCallerContext.Current = AdminB;

            for (var i = 0; i < 3; i++)
            {
                (await KeyValues.LookupAsync(OrgB, Project, View, "db.password", includeSecrets: true))!.Value.Should().Be("b-secret-2");
            }
        }
        finally
        {
            KeyValueCallerContext.Current = null;
        }

        await Audit.FlushAsync();

        // the seed audits its secret writes: db.password created and updated, and db.password of prod created
        var allA = await Audit.QueryAsync(OrgA, Project, new KeyValueAuditQuery());
        allA.Items.Select(entry => entry.Operation).Should().BeEquivalentTo(
        [
            KeyValueAuditOperation.Create,
            KeyValueAuditOperation.Update,
            KeyValueAuditOperation.Create,
            KeyValueAuditOperation.Reveal,
            KeyValueAuditOperation.BindingRead,
        ]);
        allA.Items.Should().OnlyContain(entry => entry.Actor == AdminA.Actor);
        allA.Items.Where(entry => entry.Operation != KeyValueAuditOperation.BindingRead).Should().OnlyContain(entry => entry.CorrelationId == "corr-a");

        var allB = await Audit.QueryAsync(OrgB, Project, new KeyValueAuditQuery());
        allB.Items.Should().HaveCount(7, "3 writes, 3 reveals and 1 aggregated binding read");
        allB.Items.Should().OnlyContain(entry => entry.Actor == AdminB.Actor);

        var revealsA = await Audit.QueryAsync(OrgA, Project, new KeyValueAuditQuery { Operation = KeyValueAuditOperation.Reveal });
        revealsA.Items.Should().ContainSingle().Which.Version.Should().Be(2);
        (await Audit.QueryAsync(OrgB, Project, new KeyValueAuditQuery { Operation = KeyValueAuditOperation.Reveal })).Items.Should().HaveCount(3);

        var bindingA = await Audit.QueryAsync(OrgA, Project, new KeyValueAuditQuery { Operation = KeyValueAuditOperation.BindingRead });
        bindingA.Items.Should().ContainSingle().Which.Count.Should().Be(2, "B's three reads of the same key count in B");
        var bindingB = await Audit.QueryAsync(OrgB, Project, new KeyValueAuditQuery { Operation = KeyValueAuditOperation.BindingRead });
        bindingB.Items.Should().ContainSingle().Which.Count.Should().Be(3);

        var prodA = await Audit.QueryAsync(OrgA, Project, new KeyValueAuditQuery { View = Prod });
        prodA.Items.Should().ContainSingle().Which.Operation.Should().Be(KeyValueAuditOperation.Create);
        var createsB = await Audit.QueryAsync(OrgB, Project, new KeyValueAuditQuery { Key = "db.password", Operation = KeyValueAuditOperation.Create });
        createsB.Items.Should().HaveCount(2).And.OnlyContain(entry => entry.Actor == AdminB.Actor);
    }

    private static string PairsView(string tag) => $"pairs-{tag}";

    private static KeyValueWrite Public(string value) => new() { Protection = KeyValueProtection.Public, Value = value };

    private static KeyValueWrite Secret(string value) => new() { Protection = KeyValueProtection.Standard, Value = value };

    private static IEnumerable<string> Counts(IReadOnlyDictionary<string, int> counts)
    {
        return counts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}");
    }

    private static string DiffText(DiffResult diff)
    {
        return string.Join('\n', diff.Hunks.SelectMany(hunk => hunk.Lines).Select(line => line.Content));
    }

    private async Task SeedBothAsync()
    {
        await SeedAsync(OrgA, "a", AdminA, HostVersionsA);
        await SeedAsync(OrgB, "b", AdminB, HostVersionsB);
    }

    // The same project, views and keys in every organization; the values carry the organization's tag. Writes: hostVersions + 9.
    private async Task SeedAsync(string organization, string tag, KeyValueCaller caller, int hostVersions)
    {
        for (var version = 1; version <= hostVersions; version++)
        {
            await KeyValues.SetAsync(organization, Project, View, "db.host", Public($"{tag}-host-{version}"), caller);
        }

        await KeyValues.SetAsync(organization, Project, View, "db.password", Secret($"{tag}-secret-1"), caller);
        await KeyValues.SetAsync(organization, Project, View, "db.password", Secret($"{tag}-secret-2"), caller);
        await KeyValues.SetAsync(organization, Project, View, "app.name", Public($"{tag}-app"), caller);
        await KeyValues.SetAsync(organization, Project, View, "old.key", Public($"{tag}-old"), caller);
        await KeyValues.DeleteAsync(organization, Project, View, "old.key", new KeyValueDeleteOptions(), caller);
        await KeyValues.SetAsync(organization, Project, View, $"only.{tag}", Public($"{tag}-only"), caller);
        await KeyValues.SetAsync(organization, Project, Prod, "db.host", Public($"{tag}-prod-host"), caller);
        await KeyValues.SetAsync(organization, Project, Prod, "db.password", Secret($"{tag}-prod-secret"), caller);
        await KeyValues.SetAsync(organization, Project, PairsView(tag), "k", Public($"{tag}-pairs"), caller);
    }

    // The organizations and their project as access points in core: the usage snapshot finds the organizations there,
    // and a project's key/value policy lives on its access point.
    private async Task CreateAccessPointsAsync()
    {
        var core = Repositories.GetCore().Container;

        foreach (var key in new[] { OrgA, OrgB, $"{OrgA}.{Project}", $"{OrgB}.{Project}" })
        {
            await core.CreateItemAsync(new AccessPointEntity { Key = key, AccessMap = new() }, new PartitionKey("access"));
        }
    }

    // Every read of the area through one organization: its own values, versions and views, and nothing of the other one.
    private async Task AssertSeesOnlyItsOwnPairsAsync(string organization, string tag, string otherTag, int hostVersions, KeyValueCaller caller)
    {
        var currentHost = $"{tag}-host-{hostVersions}";

        var listed = await KeyValues.ListAsync(organization, Project, View, new KeyValueListRequest { IncludeValues = true });
        listed.Items.Select(item => (item.Key, item.Value)).Should().Equal(
            ("app.name", $"{tag}-app"),
            ("db.host", currentHost),
            ("db.password", null),
            ($"only.{tag}", $"{tag}-only"));
        var deleted = await KeyValues.ListAsync(organization, Project, View, new KeyValueListRequest { State = KeyValueState.Deleted, IncludeValues = true });
        deleted.Items.Select(item => (item.Key, item.Value)).Should().Equal(("old.key", $"{tag}-old"));

        var host = await KeyValues.GetAsync(organization, Project, View, "db.host", new KeyValueReadOptions(), caller);
        host!.Value.Should().Be(currentHost);
        host.Version.Should().Be((ulong)hostVersions);
        (await KeyValues.GetAsync(organization, Project, View, "db.password", new KeyValueReadOptions { Reveal = true }, caller))!.Value.Should().Be($"{tag}-secret-2");
        (await KeyValues.GetAsync(organization, Project, View, "old.key", new KeyValueReadOptions { IncludeDeleted = true }, caller))!.Value.Should().Be($"{tag}-old");
        (await KeyValues.GetAsync(organization, Project, View, $"only.{otherTag}", new KeyValueReadOptions { IncludeDeleted = true }, caller))
            .Should().BeNull("the key exists in the other organization only");

        (await KeyValues.GetVersionsAsync(organization, Project, View, "db.host")).Select(version => version.Version)
            .Should().Equal(Enumerable.Range(1, hostVersions).Select(version => (ulong)version));
        (await KeyValues.GetVersionAsync(organization, Project, View, "db.host", 1, reveal: false, caller))!.Value.Should().Be($"{tag}-host-1");
        (await KeyValues.GetVersionAsync(organization, Project, View, "db.host", (ulong)hostVersions + 1, reveal: false, caller)).Should().BeNull();
        (await KeyValues.GetVersionAsync(organization, Project, View, "db.password", 1, reveal: true, caller))!.Value.Should().Be($"{tag}-secret-1");

        DiffText(await KeyValues.GetVersionChangesAsync(organization, Project, View, "db.host", 1, 2, caller))
            .Should().Contain($"{tag}-host-1").And.Contain($"{tag}-host-2").And.NotContain($"{otherTag}-");
        DiffText(await KeyValues.GetVersionChangesAsync(organization, Project, View, "db.password", 1, 2, caller))
            .Should().Contain($"{tag}-secret-1").And.Contain($"{tag}-secret-2").And.NotContain($"{otherTag}-");

        var exported = await KeyValues.ExportAsync(organization, Project, View, new KeyValueExportRequest { IncludeSecrets = true }, caller);
        exported.Items.Select(item => (item.Key, item.Value)).Should().Equal(
            ("app.name", $"{tag}-app"),
            ("db.host", currentHost),
            ("db.password", $"{tag}-secret-2"),
            ($"only.{tag}", $"{tag}-only"));

        (await KeyValues.LookupAsync(organization, Project, View, "db.host", includeSecrets: false))!.Value.Should().Be(currentHost);
        (await KeyValues.LookupAsync(organization, Project, View, $"only.{otherTag}", includeSecrets: false)).Should().BeNull();

        var prod = await KeyValues.ListAsync(organization, Project, Prod, new KeyValueListRequest { IncludeValues = true });
        prod.Items.Select(item => (item.Key, item.Value)).Should().Equal(("db.host", $"{tag}-prod-host"), ("db.password", null));
        (await KeyValues.GetAsync(organization, Project, Prod, "db.password", new KeyValueReadOptions { Reveal = true }, caller))!.Value.Should().Be($"{tag}-prod-secret");
        (await KeyValues.ListAsync(organization, Project, PairsView(otherTag), new KeyValueListRequest { State = KeyValueState.All })).Items
            .Should().BeEmpty("the view holds pairs in the other organization only");

        // the discovery of views is an organization-wide query over the project's partitions
        var views = await Views.GetViewsAsync(organization, Project, discover: true);
        views.Select(view => view.Name).Should().Contain(new[] { View, Prod, PairsView(tag) }).And.NotContain(PairsView(otherTag));
    }

    // What the services show of an organization's pairs: every pair of its views, active and deleted, with its value
    // (secrets revealed, which audits them) and its versions.
    private async Task<List<string>> DescribeAsync(string organization, string tag, KeyValueCaller caller)
    {
        var lines = new List<string>();

        foreach (var view in new[] { View, Prod, PairsView(tag) })
        {
            foreach (var state in new[] { KeyValueState.Active, KeyValueState.Deleted })
            {
                var listed = await KeyValues.ListAsync(organization, Project, view, new KeyValueListRequest { State = state, IncludeValues = true });

                foreach (var item in listed.Items)
                {
                    var value = item.Protection.IsSecret()
                        ? (await KeyValues.GetAsync(organization, Project, view, item.Key, new KeyValueReadOptions { Reveal = true, IncludeDeleted = true }, caller))?.Value
                        : item.Value;
                    var versions = await KeyValues.GetVersionsAsync(organization, Project, view, item.Key);
                    lines.Add($"{view}/{item.Key} {state} v{item.Version} {item.Protection} {value} [{string.Join(",", versions.Select(version => version.Version))}]");
                }
            }
        }

        return lines;
    }

    // Every item of the organization with its ETag and the key of its envelope: a write creates or removes an item or
    // changes its ETag, so two equal reads mean that nothing of the organization changed in between.
    private async Task<List<StoredItem>> ReadItemsAsync(string organization)
    {
        var repository = Repositories.GetOrganizationContainer(organization);
        var query = repository.WithOrganization(new QueryDefinition(
            $"SELECT c.id, c.PartitionKey, c._etag, c.Content.Encrypted.KeyId AS KeyId FROM c WHERE c.Organization = {OrganizationContainer.OrganizationParameter}"));
        using var iterator = repository.Container.GetItemQueryIterator<StoredItem>(query, requestOptions: repository.QueryOptions());
        var items = new List<StoredItem>();

        while (iterator.HasMoreResults)
        {
            items.AddRange(await iterator.ReadNextAsync());
        }

        return items;
    }

    // The key ids of the organization's envelopes; the fake provider ends each with "/{key version}".
    private async Task<List<string>> ReadEnvelopeKeyIdsAsync(string organization)
    {
        var repository = Repositories.GetOrganizationContainer(organization);
        var query = repository.WithOrganization(new QueryDefinition(
            $"SELECT VALUE c.Content.Encrypted.KeyId FROM c WHERE c.Organization = {OrganizationContainer.OrganizationParameter} AND IS_DEFINED(c.Content.Encrypted)"));
        using var iterator = repository.Container.GetItemQueryIterator<string>(query, requestOptions: repository.QueryOptions());
        var keyIds = new List<string>();

        while (iterator.HasMoreResults)
        {
            keyIds.AddRange(await iterator.ReadNextAsync());
        }

        return keyIds;
    }

    private sealed record StoredItem
    {
        [JsonProperty("id")]
        public required string Id { get; init; }

        public required string PartitionKey { get; init; }

        [JsonProperty("_etag")]
        public required string ETag { get; init; }

        public string? KeyId { get; init; }
    }
}
