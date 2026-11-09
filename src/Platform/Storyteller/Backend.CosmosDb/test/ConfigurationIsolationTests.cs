#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.Transferring;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Tenant isolation of the configurations in the shared container: two organizations with the same project, views,
// annotation keys, template and key/value name, whose values carry the word of their organization (alpha, bravo), so a
// value of the other one in a result is visible. Every test seeds its own pair, named with a fresh suffix, so the tests
// and the reruns on a reused emulator never share an organization.
public class ConfigurationIsolationTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Project = "billing";
    private const string View = Constants.DefaultViewName;
    private const string ProdView = "prod";
    private const string HostKey = "db.host";

    private static readonly AuditActor Actor = AnnotationServiceTestExtensions.TestActor;
    private static readonly KeyValueCaller Admin = KeyValueCaller.WithSecrets(AuditActor.Account("admin"));

    // one tree in both organizations: the subject and the context share a partition, the others the responsibility's
    private static readonly AnnotationKey UnitOfExecutionKey = AnnotationKey.CreateUnitOfExecution("acme", "invoicing", "eu", "export");
    private static readonly AnnotationKey SubjectKey = UnitOfExecutionKey.GetSubjectKey();
    private static readonly AnnotationKey ContextKey = UnitOfExecutionKey.GetContextKey();
    private static readonly AnnotationKey ResponsibilityKey = UnitOfExecutionKey.GetResponsibilityKey();
    private static readonly AnnotationKey UsageKey = UnitOfExecutionKey.GetUsageKey();
    private static readonly AnnotationKey ExecutionKey = UnitOfExecutionKey.GetExecutionKey();
    private static readonly AnnotationKey UnitKey = UnitOfExecutionKey.GetUnitKey();

    // the levels of the tree with a document of their own (the unit has none)
    private static readonly AnnotationKey[] TreeKeys = [SubjectKey, ContextKey, ResponsibilityKey, UsageKey, ExecutionKey, UnitOfExecutionKey];

    private IConfigurationService Configs => Context.Services.GetRequiredService<IConfigurationService>();

    private IConfigurationTemplateService Templates => Context.Services.GetRequiredService<IConfigurationTemplateService>();

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    private IKeyValueService KeyValues => Context.Services.GetRequiredService<IKeyValueService>();

    private IContainerRepositoryProvider Repositories => Context.Services.GetRequiredService<IContainerRepositoryProvider>();

    [Fact]
    public async Task PointReads_OfTheSameKeys_AnswerEachOrganizationWithItsOwnDocuments()
    {
        var (a, b) = await SeedPairAsync();

        foreach (var tenant in new[] { a, b })
        {
            await AssertPointReadsAsync(tenant);
        }
    }

    [Fact]
    public async Task ListingsAndHistoriesOfAView_HoldOnlyTheirOwnOrganization()
    {
        var (a, b) = await SeedPairAsync();

        // the effective reads cache rows also for the levels without a document; they must stay in their organization too
        await WarmAsync(a);
        await WarmAsync(b);

        foreach (var tenant in new[] { a, b })
        {
            var listed = await ListAsync(tenant.Organization);
            var keys = listed.Select(summary => summary.AnnotationKey).ToList();

            keys.Should().OnlyHaveUniqueItems("a configuration of the other organization would repeat a key");
            keys.Should().NotContain(tenant.ForeignOnlyKey.ToString());
            ShouldNotLeakText(string.Join(",", keys), tenant);
            listed.Where(summary => summary.HasContent).Select(summary => summary.AnnotationKey).Should().BeEquivalentTo(tenant.OwnDocumentKeys);
            listed.Single(summary => summary.AnnotationKey == ResponsibilityKey.ToString()).Version.Should().Be((ulong)tenant.ResponsibilityVersions);

            (await ListAsync(tenant.Organization, annotationType: AnnotationTypeCodes.Responsibility))
                .Select(summary => summary.AnnotationKey)
                .Should().BeEquivalentTo(ResponsibilityKey.ToString(), tenant.OnlyKey.ToString());
            (await ListAsync(tenant.Organization, keyPrefix: "rst.only"))
                .Select(summary => summary.AnnotationKey)
                .Should().Equal(tenant.OnlyKey.ToString());

            var prod = (await ListAsync(tenant.Organization, ProdView)).Should().ContainSingle().Which;
            prod.AnnotationKey.Should().Be(ResponsibilityKey.ToString());
            prod.Version.Should().Be(1);

            // the histories of a view (an export): the own documents and every version of this organization only
            var histories = await HistoriesAsync(tenant.Organization);
            histories.Select(history => history.AnnotationKey).Should().BeEquivalentTo(tenant.OwnDocumentKeys);
            histories.Single(history => history.AnnotationKey == ResponsibilityKey.ToString())
                .Versions.Select(version => version.Version)
                .Should().Equal(Enumerable.Range(1, tenant.ResponsibilityVersions).Select(version => (ulong)version).ToArray());
            histories.Single(history => history.AnnotationKey == tenant.OnlyKey.ToString())
                .Current!.Content["only"]!.Value<string>().Should().Be($"{tenant.Tag}-only");
            ShouldNotLeakText(
                string.Join("\n", histories.SelectMany(history => history.Versions).Select(version => version.Content.ToString(Formatting.None))),
                tenant);

            var prodHistory = (await HistoriesAsync(tenant.Organization, ProdView)).Should().ContainSingle().Which;
            prodHistory.AnnotationKey.Should().Be(ResponsibilityKey.ToString());
            prodHistory.Current!.Content["label"]!.Value<string>().Should().Be($"{tenant.Tag}-prod");
        }
    }

    [Fact]
    public async Task Writes_ThroughOneOrganization_LeaveEveryItemOfTheOtherUnchanged()
    {
        var (a, b) = await SeedPairAsync();

        // the second organization has cached calculations, which a leaking invalidation would clear
        await WarmAsync(b);
        var effectiveBefore = (await Configs.GetRawConfigurationAsync(b.Key(UnitOfExecutionKey)))!.Content.ToString(Formatting.None);
        var before = await SnapshotAsync(b.Organization);

        // a first write and an update of an ancestor invalidate its descendants across the partitions of the organization
        await WriteAsync(a.Key(SubjectKey), """{ "owner": "alpha-subject-2" }""");
        await Configs.PatchConfigurationAsync(
            a.Key(ResponsibilityKey),
            JArray.Parse("""[ { "op": "replace", "path": "/label", "value": "alpha-patched" } ]"""),
            Actor);
        var import = await Configs.ImportConfigurationAsync(
            a.Key(UsageKey),
            JObject.Parse("""{ "plan": "alpha-imported" }"""),
            new ViewImportOptions { Actor = Actor },
            new ViewImportOverlay());
        import.Outcome.Should().Be(ViewImportOutcome.Updated);

        // a template invalidates every configuration of the view that merges it
        await Templates.CreateOrUpdateTemplateAsync(
            a.Organization,
            Project,
            View,
            AnnotationTypeCodes.Responsibility,
            JObject.Parse("""{ "tier": "alpha-tier-2" }"""),
            Actor);
        (await Configs.ClearConfigurationAsync(a.Key(ContextKey), Actor)).Should().BeTrue();
        await Configs.DeleteAsync(a.Key(ExecutionKey), Actor);
        await Annotations.DeleteAnnotationAsync(a.Key(UnitOfExecutionKey), Actor);
        (await Annotations.RestoreAnnotationsAsync(a.Organization, Project, View, [UnitOfExecutionKey], Actor)).Failed.Should().BeEmpty();
        var effectiveA = (await Configs.GetRawConfigurationAsync(a.Key(UnitOfExecutionKey)))!.Content;

        (await SnapshotAsync(b.Organization)).Should().BeEquivalentTo(before, "no write through the first organization reaches an item of the second");

        // the writes did happen, in the first organization
        ShouldNotLeak(effectiveA, a);
        effectiveA["owner"]!.Value<string>().Should().Be("alpha-subject-2");
        effectiveA["label"]!.Value<string>().Should().Be("alpha-patched");
        effectiveA["plan"]!.Value<string>().Should().Be("alpha-imported");
        effectiveA["tier"]!.Value<string>().Should().Be("alpha-tier-2");
        effectiveA["own"]!.Value<string>().Should().Be("alpha-unit-of-execution");
        effectiveA.Should().NotContainKey("endpoint");
        effectiveA.Should().NotContainKey("schedule");

        (await Configs.GetRawConfigurationAsync(b.Key(UnitOfExecutionKey)))!.Content.ToString(Formatting.None).Should().Be(effectiveBefore);
        (await Configs.GetRawConfigurationAsync(b.Key(ContextKey)))!.Content["endpoint"]!.Value<string>().Should().Be("https://bravo.example/eu");
        (await Configs.GetConfigurationVersionsAsync(b.Key(ResponsibilityKey))).Select(version => version.Version).Should().Equal(1u);
        (await Configs.GetConfigurationVersionsAsync(b.Key(ExecutionKey))).Should().HaveCount(1);
        (await Templates.GetTemplateAsync(b.Organization, Project, View, AnnotationTypeCodes.Responsibility))!
            .Content["tier"]!.Value<string>().Should().Be("bravo-tier");
    }

    [Fact]
    public async Task SoftDeleteRestoreAndPurge_InOneOrganization_HideAndRemoveNothingOfTheOther()
    {
        var (a, b) = await SeedPairAsync();
        await WarmAsync(b);
        var before = await SnapshotAsync(b.Organization);

        var deletion = await Annotations.DeleteAnnotationAsync(a.Key(SubjectKey), Actor);

        var hidden = new[] { SubjectKey, ContextKey, UsageKey, ExecutionKey, UnitOfExecutionKey }.Select(key => key.ToString()).ToArray();
        deletion.DeletedKeys.Should().BeEquivalentTo(hidden);
        (await SnapshotAsync(b.Organization)).Should().BeEquivalentTo(before, "a soft delete marks only the items of its organization");

        // the first organization hides the configurations of the deleted tree
        (await Configs.GetRawConfigurationAsync(a.Key(SubjectKey))).Should().BeNull();
        (await Configs.HasConfigurationContentAsync(a.Key(UsageKey))).Should().BeFalse();
        (await Configs.GetConfigurationVersionsAsync(a.Key(SubjectKey))).Should().BeEmpty();
        (await ListAsync(a.Organization)).Select(summary => summary.AnnotationKey).Should().NotContain(hidden);
        (await HistoriesAsync(a.Organization)).Select(history => history.AnnotationKey)
            .Should().BeEquivalentTo(ResponsibilityKey.ToString(), a.OnlyKey.ToString());

        // the second keeps them in every read, listing and history: the deleted keys of the first never hide its own
        (await Configs.GetRawConfigurationAsync(b.Key(SubjectKey)))!.Content["owner"]!.Value<string>().Should().Be("bravo-subject");
        (await Configs.HasConfigurationContentAsync(b.Key(UsageKey))).Should().BeTrue();
        (await Configs.GetConfigurationVersionsAsync(b.Key(SubjectKey))).Should().HaveCount(1);
        (await ListAsync(b.Organization)).Where(summary => summary.HasContent).Select(summary => summary.AnnotationKey)
            .Should().BeEquivalentTo(b.OwnDocumentKeys);
        (await HistoriesAsync(b.Organization)).Select(history => history.AnnotationKey).Should().BeEquivalentTo(b.OwnDocumentKeys);

        before = await SnapshotAsync(b.Organization);
        var restore = await Annotations.RestoreAnnotationsAsync(a.Organization, Project, View, [SubjectKey], Actor);

        restore.Failed.Should().BeEmpty();
        (await SnapshotAsync(b.Organization)).Should().BeEquivalentTo(before, "a restore clears the marks of its organization only");
        (await Configs.GetRawConfigurationAsync(a.Key(SubjectKey)))!.Content["owner"]!.Value<string>().Should().Be("alpha-subject");
        (await HistoriesAsync(a.Organization)).Select(history => history.AnnotationKey).Should().BeEquivalentTo(a.OwnDocumentKeys);

        // the same key in the other view, deleted and purged in the first organization
        var prodA = a.Key(ResponsibilityKey, ProdView);
        before = await SnapshotAsync(b.Organization);
        await Annotations.DeleteAnnotationAsync(prodA, Actor);
        var purge = await Annotations.PurgeAnnotationAsync(prodA, Actor);

        purge.PurgedKeys.Should().Equal(ResponsibilityKey.ToString());
        (await SnapshotAsync(b.Organization)).Should().BeEquivalentTo(before, "a purge removes the items of its organization only");
        (await Configs.GetRawConfigurationAsync(prodA)).Should().BeNull();
        (await Configs.GetRawConfigurationAsync(b.Key(ResponsibilityKey, ProdView)))!.Content["label"]!.Value<string>().Should().Be("bravo-prod");
        (await ListAsync(b.Organization, ProdView)).Should().ContainSingle().Which.AnnotationKey.Should().Be(ResponsibilityKey.ToString());
    }

    [Fact]
    public async Task VersionNumbers_CountOnlyTheArchiveOfTheirOwnOrganization()
    {
        var (a, b) = NewPair();
        var ledger = AnnotationKey.CreateResponsibility("ledger");

        foreach (var tenant in new[] { a, b })
        {
            await Annotations.CreateAnnotationsFromStringAsync(tenant.Organization, Project, View, [ledger.ToString()], Actor);
        }

        for (var version = 1; version <= 3; version++)
        {
            (await WriteAsync(a.Key(ledger), $$"""{ "entry": "alpha-{{version}}" }""")).Version.Should().Be((ulong)version);
        }

        // the clear archives the third version, so the next write of the first organization continues at the fourth
        (await Configs.ClearConfigurationAsync(a.Key(ledger), Actor)).Should().BeTrue();

        (await WriteAsync(b.Key(ledger), """{ "entry": "bravo-1" }""")).Version.Should().Be(1, "the archive of the same key in the other organization is not this one's");
        (await WriteAsync(a.Key(ledger), """{ "entry": "alpha-4" }""")).Version.Should().Be(4);

        (await Configs.GetConfigurationVersionsAsync(a.Key(ledger))).Select(version => version.Version).Should().Equal(1u, 2u, 3u, 4u);
        (await Configs.GetConfigurationVersionsAsync(b.Key(ledger))).Select(version => version.Version).Should().Equal(1u);
        (await Configs.GetConfigurationVersionContentAsync(a.Key(ledger), 3))!.Content["entry"]!.Value<string>().Should().Be("alpha-3");
        (await Configs.GetConfigurationVersionContentAsync(b.Key(ledger), 3)).Should().BeNull();
        (await Configs.GetRawConfigurationAsync(b.Key(ledger)))!.Content["entry"]!.Value<string>().Should().Be("bravo-1");
    }

    [Fact]
    public async Task Dependencies_RecordedInOneOrganization_FollowOnlyItsItems()
    {
        var (a, b) = await SeedPairAsync();
        var usageA = a.Key(UsageKey);
        var subjectA = a.Key(SubjectKey);

        var (resolved, dependencies) = await ResolveAsync(usageA);

        resolved.Content["host"]!.Value<string>().Should().Be("alpha-host");
        resolved.Content["steward"]!.Value<string>().Should().Be("alpha-steward");
        dependencies.VolatileReason.Should().BeNull();
        var recorded = dependencies.ToList();
        recorded.Should().Contain(new BindingDependency(
            BindingDependencyKinds.Annotation,
            View,
            SubjectKey.ToString(),
            await ETagAsync(a.Organization, subjectA.GetCosmosItemId(), subjectA.GetPartitionKey())));
        recorded.Should().Contain(new BindingDependency(
            BindingDependencyKinds.KeyValue,
            View,
            HostKey,
            await ETagAsync(a.Organization, $"kv.{HostKey}", PartitionKeys.GetKeyValues(Project, View))));

        (await Configs.AreDependenciesUnchangedAsync(usageA, recorded)).Should().BeTrue();
        (await Configs.AreDependenciesUnchangedAsync(b.Key(UsageKey), recorded))
            .Should().BeFalse("the markers are the ones of the items of the first organization, the second has items of its own");

        // the same names change in the other organization
        await SetHostAsync(b, "bravo-host-2");
        await SetStewardAsync(b, "bravo-steward-2");

        (await Configs.AreDependenciesUnchangedAsync(usageA, recorded)).Should().BeTrue("nothing of the first organization changed");
        (await ResolveAsync(usageA)).Resolved.Content["host"]!.Value<string>().Should().Be("alpha-host");

        // and in its own
        await SetHostAsync(a, "alpha-host-2");

        (await Configs.AreDependenciesUnchangedAsync(usageA, recorded)).Should().BeFalse();
    }

    private static (Tenant A, Tenant B) NewPair()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        return (
            new Tenant($"iso-config-a-{suffix}", "alpha", "bravo", ResponsibilityVersions: 2),
            new Tenant($"iso-config-b-{suffix}", "bravo", "alpha", ResponsibilityVersions: 1));
    }

    // A value of the other organization carries its word.
    private static void ShouldNotLeak(JToken token, Tenant tenant)
    {
        ShouldNotLeakText(token.ToString(Formatting.None), tenant);
    }

    private static void ShouldNotLeakText(string text, Tenant tenant)
    {
        text.Should().NotContain(tenant.Foreign, $"a result of {tenant.Organization} holds only its own values");
    }

    private static string Lines(DiffResult diff)
    {
        return string.Join("\n", diff.Hunks.SelectMany(hunk => hunk.Lines).Select(line => line.Content));
    }

    private async Task AssertPointReadsAsync(Tenant tenant)
    {
        var tag = tenant.Tag;
        var unitOfExecution = tenant.Key(UnitOfExecutionKey);
        var responsibility = tenant.Key(ResponsibilityKey);

        // the effective document: every level of the tree and the template, all of this organization
        var raw = await Configs.GetRawConfigurationAsync(unitOfExecution);
        raw.Should().NotBeNull();
        ShouldNotLeak(raw!.Content, tenant);
        raw.Content["own"]!.Value<string>().Should().Be($"{tag}-unit-of-execution");
        raw.Content["schedule"]!.Value<string>().Should().Be($"{tag}-schedule");
        raw.Content["endpoint"]!.Value<string>().Should().Be($"https://{tag}.example/eu");
        raw.Content["plan"]!.Value<string>().Should().Be($"{tag}-plan");
        raw.Content["owner"]!.Value<string>().Should().Be($"{tag}-subject");
        raw.Content["label"]!.Value<string>().Should().Be($"{tag}-responsibility");
        raw.Content["retries"]!.Value<int>().Should().Be(tenant.ResponsibilityVersions);
        raw.Content["tier"]!.Value<string>().Should().Be($"{tag}-tier");

        // the bindings read the key/value pair and the annotation of this organization
        var resolved = await Configs.GetResolvedConfigurationAsync(unitOfExecution);
        ShouldNotLeak(resolved!.Content, tenant);
        resolved.Content["host"]!.Value<string>().Should().Be($"{tag}-host");
        resolved.Content["steward"]!.Value<string>().Should().Be($"{tag}-steward");

        (await Configs.HasConfigurationContentAsync(tenant.Key(tenant.OnlyKey))).Should().BeTrue();
        (await Configs.HasConfigurationContentAsync(tenant.Key(tenant.ForeignOnlyKey))).Should().BeFalse("only the other organization has it");
        (await Configs.GetRawConfigurationAsync(tenant.Key(tenant.ForeignOnlyKey))).Should().BeNull();

        var hierarchy = await Configs.GetConfigurationHierarchyViewAsync(unitOfExecution);
        ShouldNotLeak(hierarchy!.Content, tenant);
        hierarchy.Content[SubjectKey.ToString()]!["owner"]!.Value<string>().Should().Be($"{tag}-subject");
        hierarchy.Content[ResponsibilityKey.ToString()]!["label"]!.Value<string>().Should().Be($"{tag}-responsibility");
        hierarchy.Content[UnitOfExecutionKey.ToString()]!["own"]!.Value<string>().Should().Be($"{tag}-unit-of-execution");

        var composition = await Configs.GetConfigurationCompositionAsync(unitOfExecution);
        composition.Should().NotBeNull();
        composition!.Content.ToString(Formatting.None).Should().Be(raw.Content.ToString(Formatting.None));
        composition.Sources.Single(candidate => candidate.Id == $"template:{AnnotationTypeCodes.Responsibility}")
            .Content!["tier"]!.Value<string>().Should().Be($"{tag}-tier");

        foreach (var source in composition.Sources.Where(candidate => candidate.Content is not null))
        {
            ShouldNotLeak(source.Content!, tenant);
        }

        // the versions of the responsibility: two in the first organization, one in the second
        (await Configs.GetConfigurationVersionsAsync(responsibility)).Select(version => version.Version)
            .Should().Equal(Enumerable.Range(1, tenant.ResponsibilityVersions).Select(version => (uint)version).ToArray());

        for (var version = 1u; version <= (uint)tenant.ResponsibilityVersions; version++)
        {
            var content = await Configs.GetConfigurationVersionContentAsync(responsibility, version);
            content!.Content["retries"]!.Value<int>().Should().Be((int)version);
            content.Content["label"]!.Value<string>().Should().Be($"{tag}-responsibility");
        }

        (await Configs.GetConfigurationVersionContentAsync(responsibility, (uint)tenant.ResponsibilityVersions + 1)).Should().BeNull();

        var changes = await Configs.GetConfigurationVersionChangesAsync(responsibility, (uint)tenant.ResponsibilityVersions);
        changes.Stats.Additions.Should().BeGreaterThan(0);
        ShouldNotLeakText(Lines(changes), tenant);

        // the same key in the other view of the same organization
        var viewChanges = Lines(await Configs.GetConfigurationViewChangesAsync(responsibility, ProdView));
        viewChanges.Should().Contain($"{tag}-responsibility").And.Contain($"{tag}-prod");
        ShouldNotLeakText(viewChanges, tenant);
    }

    // The same annotations, template, key/value pair and configurations in both organizations, with the values of their
    // own word; the responsibility has two versions in the first organization and one in the second.
    private async Task<(Tenant A, Tenant B)> SeedPairAsync()
    {
        var (a, b) = NewPair();
        await SeedAsync(a);
        await SeedAsync(b);
        return (a, b);
    }

    private async Task SeedAsync(Tenant tenant)
    {
        var organization = tenant.Organization;
        var tag = tenant.Tag;

        await Annotations.CreateAnnotationsFromStringAsync(organization, Project, View, [UnitOfExecutionKey.ToString(), tenant.OnlyKey.ToString()], Actor);
        await Annotations.CreateAnnotationsFromStringAsync(organization, Project, ProdView, [ResponsibilityKey.ToString()], Actor);
        await SetStewardAsync(tenant, $"{tag}-steward");
        await SetHostAsync(tenant, $"{tag}-host");
        await Templates.CreateOrUpdateTemplateAsync(
            organization,
            Project,
            View,
            AnnotationTypeCodes.Responsibility,
            JObject.Parse($$"""{ "tier": "{{tag}}-tier" }"""),
            Actor);

        await WriteAsync(tenant.Key(SubjectKey), $$"""{ "owner": "{{tag}}-subject", "region": "{{tag}}-region" }""");
        await WriteAsync(tenant.Key(ContextKey), $$"""{ "endpoint": "https://{{tag}}.example/eu" }""");

        for (var version = 1; version <= tenant.ResponsibilityVersions; version++)
        {
            await WriteAsync(tenant.Key(ResponsibilityKey), $$"""{ "retries": {{version}}, "label": "{{tag}}-responsibility" }""");
        }

        await WriteAsync(tenant.Key(UsageKey), $$"""{ "plan": "{{tag}}-plan", "host": "@db.host", "steward": "@annotation(\"/steward\", \"Subject\")" }""");
        await WriteAsync(tenant.Key(ExecutionKey), $$"""{ "schedule": "{{tag}}-schedule" }""");
        await WriteAsync(tenant.Key(UnitOfExecutionKey), $$"""{ "own": "{{tag}}-unit-of-execution" }""");
        await WriteAsync(tenant.Key(tenant.OnlyKey), $$"""{ "only": "{{tag}}-only" }""");
        await WriteAsync(tenant.Key(ResponsibilityKey, ProdView), $$"""{ "label": "{{tag}}-prod" }""");
    }

    // Effective reads cache their calculation on the items, and create the items of the levels without a document.
    private async Task WarmAsync(Tenant tenant)
    {
        foreach (var key in TreeKeys.Append(UnitKey).Append(tenant.OnlyKey))
        {
            await Configs.GetRawConfigurationAsync(tenant.Key(key));
        }

        await Configs.GetRawConfigurationAsync(tenant.Key(ResponsibilityKey, ProdView));
    }

    private Task<Configuration> WriteAsync(FullKey key, string content)
    {
        return Configs.CreateOrUpdateConfigurationAsync(key, JObject.Parse(content), Actor);
    }

    private Task SetHostAsync(Tenant tenant, string value)
    {
        return KeyValues.SetAsync(
            tenant.Organization,
            Project,
            View,
            HostKey,
            new KeyValueWrite { Protection = KeyValueProtection.Public, Value = value },
            Admin);
    }

    private async Task SetStewardAsync(Tenant tenant, string value)
    {
        var subject = (await Annotations.GetAnnotationAsync(tenant.Key(SubjectKey)))!;
        await Annotations.UpdateAnnotationAsync(tenant.Organization, subject with { Values = new Dictionary<string, object> { ["steward"] = value } }, Actor);
    }

    private async Task<(Configuration Resolved, BindingDependencies Dependencies)> ResolveAsync(FullKey key)
    {
        var effective = (await Configs.GetRawConfigurationAsync(key))!;
        var dependencies = new BindingDependencies();
        var resolved = await Configs.ResolveConfigurationAsync(key, effective, includeSecrets: false, dependencies: dependencies);
        return (resolved, dependencies);
    }

    private async Task<List<ConfigurationSummary>> ListAsync(string organization, string view = View, string? annotationType = null, string? keyPrefix = null)
    {
        var summaries = new List<ConfigurationSummary>();
        string? continuationToken = null;

        do
        {
            var page = await Configs.ListConfigurationsAsync(organization, Project, view, annotationType, keyPrefix, continuationToken);
            summaries.AddRange(page.Configurations);
            continuationToken = page.ContinuationToken;
        }
        while (!string.IsNullOrEmpty(continuationToken));

        return summaries;
    }

    private Task<IReadOnlyList<ConfigurationHistory>> HistoriesAsync(string organization, string view = View)
    {
        return Configs.GetConfigurationHistoriesAsync(organization, Project, view, TestContext.Current.CancellationToken);
    }

    // Every item of the organization by partition and id, with its ETag: a write that reaches an item changes its ETag,
    // a delete removes it and a create adds one.
    private async Task<Dictionary<string, string>> SnapshotAsync(string organization)
    {
        var repository = Repositories.GetOrganizationContainer(organization);
        var query = repository.WithOrganization(new QueryDefinition(
            "SELECT c.id, c.PartitionKey, c._etag FROM c WHERE c.Organization = @organization"));
        var items = new Dictionary<string, string>(StringComparer.Ordinal);
        using var iterator = repository.Container.GetItemQueryIterator<JObject>(query, requestOptions: repository.QueryOptions());

        while (iterator.HasMoreResults)
        {
            foreach (var item in await iterator.ReadNextAsync())
            {
                items.Add($"{item.Value<string>("PartitionKey")}/{item.Value<string>("id")}", item.Value<string>("_etag")!);
            }
        }

        items.Should().NotBeEmpty($"{organization} has items");
        return items;
    }

    private async Task<string> ETagAsync(string organization, string id, string partitionKey)
    {
        var repository = Repositories.GetOrganizationContainer(organization);
        using var response = await repository.Container.ReadItemStreamAsync(id, repository.Key(partitionKey));
        response.IsSuccessStatusCode.Should().BeTrue($"the item {id} exists in {organization}");
        return response.Headers.ETag;
    }

    // One organization of a pair, the word its values carry and the word of the other one.
    private sealed record Tenant(string Organization, string Tag, string Foreign, int ResponsibilityVersions)
    {
        // a responsibility only this organization has, and the one only the other has
        public AnnotationKey OnlyKey => AnnotationKey.CreateResponsibility($"only-{Tag}");

        public AnnotationKey ForeignOnlyKey => AnnotationKey.CreateResponsibility($"only-{Foreign}");

        // the configurations with a document of their own in the default view
        public string[] OwnDocumentKeys => [.. TreeKeys.Select(key => key.ToString()), OnlyKey.ToString()];

        public FullKey Key(AnnotationKey annotation, string view = View)
        {
            return FullKey.Create(annotation, Organization, Project, view);
        }
    }
}
