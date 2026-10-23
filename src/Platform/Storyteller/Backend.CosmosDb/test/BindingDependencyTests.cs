#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Binding;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.KeyValues;
using _42.Platform.Storyteller.KeyValues.Audit;
using _42.Platform.Storyteller.KeyValues.Usage;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The storage markers that a resolve records, and their revalidation without a lookup.
public class BindingDependencyTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Org = TestConstants.Organization;
    private const string View = Constants.DefaultViewName;

    private static readonly KeyValueCaller Admin = KeyValueCaller.WithSecrets(AuditActor.Account("admin"));

    private IKeyValueService KeyValues => Context.Services.GetRequiredService<IKeyValueService>();

    private CountingKeyValueService Counting => Context.Services.GetRequiredService<CountingKeyValueService>();

    private IConfigurationService Configs => Context.Services.GetRequiredService<IConfigurationService>();

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    private Container Container => Context.Services.GetRequiredService<IContainerRepositoryProvider>().GetOrganizationContainer(Org).Container;

    [Fact]
    public async Task Resolve_RecordsTheItemETagsAndRevalidatesWithoutALookup()
    {
        var project = NewProject();
        await Set(project, "db.host", KeyValueProtection.Public, "sql.internal");
        await Set(project, "db.password", KeyValueProtection.Standard, "hunter2");
        await Set(project, "gone.key", KeyValueProtection.Public, "x");
        await KeyValues.DeleteAsync(Org, project, View, "gone.key", new KeyValueDeleteOptions(), Admin);
        var (key, subjectKey) = await CreateUsageAsync(project, """
            {
                "host": "@DB.Host",
                "password": "@db.password",
                "gone": "@gone.key",
                "missing": "@nope.key",
                "title": "@annotation(\"/title\")",
                "owner": "@annotation(\"/owner\", \"Subject\")"
            }
            """);

        var (resolved, dependencies) = await ResolveAsync(key, includeSecrets: false);

        resolved.Content["host"]!.Value<string>().Should().Be("sql.internal");
        resolved.Content["password"]!.Value<string>().Should().Be("@db.password", "the secret is hidden from this read");
        resolved.Content["title"]!.Value<string>().Should().Be("usage title");
        resolved.Content["owner"]!.Value<string>().Should().Be("team-a");
        dependencies.VolatileReason.Should().BeNull();

        var recorded = dependencies.ToList();
        recorded.Should().BeEquivalentTo(
        [
            new BindingDependency(BindingDependencyKinds.Annotation, View, key.Annotation.ToString(), await ETagAsync(key.GetCosmosItemId(), key.GetPartitionKey())),
            new BindingDependency(BindingDependencyKinds.Annotation, View, subjectKey.Annotation.ToString(), await ETagAsync(subjectKey.GetCosmosItemId(), subjectKey.GetPartitionKey())),
            new BindingDependency(BindingDependencyKinds.KeyValue, View, "db.host", await KeyValueETagAsync(project, "db.host")),
            new BindingDependency(BindingDependencyKinds.KeyValue, View, "db.password", await KeyValueETagAsync(project, "db.password")),
            new BindingDependency(BindingDependencyKinds.KeyValue, View, "gone.key", await KeyValueETagAsync(project, "gone.key")),
            new BindingDependency(BindingDependencyKinds.KeyValue, View, "nope.key", BindingDependency.Absent),
        ]);
        key.GetPartitionKey().Should().NotBe(subjectKey.GetPartitionKey(), "the subject lives in another partition");

        Counting.ResetLookups();
        (await Configs.AreDependenciesUnchangedAsync(key, recorded)).Should().BeTrue();
        new[] { "db.host", "db.password", "gone.key", "nope.key" }.Should().OnlyContain(name => Counting.GetLookupCount(name) == 0);
    }

    [Fact]
    public async Task Revalidation_NeitherMetersNorAudits()
    {
        var project = NewProject();
        await Set(project, "db.password", KeyValueProtection.Standard, "hunter2");
        var key = await CreateResponsibilityAsync(project, """{ "password": "@db.password" }""");
        var usage = Context.Services.GetRequiredService<IKeyValueUsageService>();
        var audit = Context.Services.GetRequiredService<IKeyValueAuditLog>();
        KeyValueCallerContext.Current = new KeyValueCaller(AuditActor.Machine("m1"), true, true, KeyValueChannel.MachineMtls);

        try
        {
            var (_, dependencies) = await ResolveAsync(key, includeSecrets: true);
            await usage.FlushAsync();
            var readsBefore = await BindingMetersAsync(usage, project);
            var auditBefore = await audit.QueryAsync(Org, project, new KeyValueAuditQuery { Operation = KeyValueAuditOperation.BindingRead });

            (await Configs.AreDependenciesUnchangedAsync(key, dependencies.ToList())).Should().BeTrue();
            await usage.FlushAsync();

            (await BindingMetersAsync(usage, project)).Should().Be(readsBefore);
            readsBefore.Should().Be((1, 1), "the resolve looked the secret up and revealed it once");
            var auditAfter = await audit.QueryAsync(Org, project, new KeyValueAuditQuery { Operation = KeyValueAuditOperation.BindingRead });
            auditAfter.Items.Sum(item => item.Count).Should().Be(auditBefore.Items.Sum(item => item.Count));
        }
        finally
        {
            KeyValueCallerContext.Current = null;
        }
    }

    [Fact]
    public async Task Revalidation_AWriteThatChangesNothing_KeepsTheMarker()
    {
        var project = NewProject();
        await Set(project, "db.host", KeyValueProtection.Public, "sql.internal");
        var key = await CreateResponsibilityAsync(project, """{ "host": "@db.host" }""");
        var (_, dependencies) = await ResolveAsync(key, includeSecrets: false);

        await Set(project, "db.host", KeyValueProtection.Public, "sql.internal");

        (await Configs.AreDependenciesUnchangedAsync(key, dependencies.ToList())).Should().BeTrue();
    }

    [Fact]
    public async Task Revalidation_DetectsAnUpdate()
    {
        await AssertDetectedAsync(project => Set(project, "db.host", KeyValueProtection.Public, "sql.other"));
    }

    [Fact]
    public async Task Revalidation_DetectsASoftDelete()
    {
        await AssertDetectedAsync(project => KeyValues.DeleteAsync(Org, project, View, "db.host", new KeyValueDeleteOptions(), Admin));
    }

    [Fact]
    public async Task Revalidation_DetectsARestore()
    {
        await AssertDetectedAsync(
            project => KeyValues.RestoreAsync(Org, project, View, new KeyValueBulkRequest { Keys = ["db.host"] }, Admin),
            prepare: project => KeyValues.DeleteAsync(Org, project, View, "db.host", new KeyValueDeleteOptions(), Admin));
    }

    [Fact]
    public async Task Revalidation_DetectsAPurgeAndARecreate()
    {
        var project = NewProject();
        await Set(project, "db.host", KeyValueProtection.Public, "sql.internal");
        var key = await CreateResponsibilityAsync(project, """{ "host": "@db.host" }""");
        var recorded = (await ResolveAsync(key, includeSecrets: false)).Dependencies.ToList();

        await KeyValues.DeleteAsync(Org, project, View, "db.host", new KeyValueDeleteOptions { Permanent = true }, Admin);
        (await Configs.AreDependenciesUnchangedAsync(key, recorded)).Should().BeFalse("the pair is gone");

        await Set(project, "db.host", KeyValueProtection.Public, "sql.internal");
        (await Configs.AreDependenciesUnchangedAsync(key, recorded)).Should().BeFalse("the recreated pair is another item, even at version 1 with the same value");
    }

    [Fact]
    public async Task Revalidation_DetectsAProtectionChange()
    {
        await AssertDetectedAsync(project => Set(project, "db.host", KeyValueProtection.Standard, "sql.internal"));
    }

    [Fact]
    public async Task Revalidation_DetectsAnAnnotationUpdate()
    {
        var project = NewProject();
        var (key, subjectKey) = await CreateUsageAsync(project, """{ "owner": "@annotation(\"/owner\", \"Subject\")" }""");
        var recorded = (await ResolveAsync(key, includeSecrets: false)).Dependencies.ToList();
        (await Configs.AreDependenciesUnchangedAsync(key, recorded)).Should().BeTrue();

        var subject = (await Annotations.GetAnnotationAsync(subjectKey))!;
        await Annotations.UpdateAnnotationAsync(Org, subject with { Values = new Dictionary<string, object> { ["owner"] = "team-b" } });

        (await Configs.AreDependenciesUnchangedAsync(key, recorded)).Should().BeFalse();
    }

    private static string NewProject()
    {
        return $"dep-{Guid.NewGuid():N}"[..30];
    }

    private static async Task<(long ReadsBinding, long Reveals)> BindingMetersAsync(IKeyValueUsageService usage, string project)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var report = await usage.GetReportAsync(Org, project, today, today);
        var day = report.Days.SingleOrDefault()?.Values ?? new Dictionary<string, long>();
        return (day.GetValueOrDefault(KeyValueUsageMeters.ReadsBinding), day.GetValueOrDefault(KeyValueUsageMeters.Reveals));
    }

    private async Task AssertDetectedAsync(Func<string, Task> change, Func<string, Task>? prepare = null)
    {
        var project = NewProject();
        await Set(project, "db.host", KeyValueProtection.Public, "sql.internal");
        var key = await CreateResponsibilityAsync(project, """{ "host": "@db.host" }""");

        if (prepare is not null)
        {
            await prepare(project);
        }

        var recorded = (await ResolveAsync(key, includeSecrets: false)).Dependencies.ToList();
        (await Configs.AreDependenciesUnchangedAsync(key, recorded)).Should().BeTrue();

        await change(project);

        (await Configs.AreDependenciesUnchangedAsync(key, recorded)).Should().BeFalse();
    }

    private async Task<(Configuration Resolved, BindingDependencies Dependencies)> ResolveAsync(FullKey key, bool includeSecrets)
    {
        var effective = (await Configs.GetRawConfigurationAsync(key))!;
        var dependencies = new BindingDependencies();
        var resolved = await Configs.ResolveConfigurationAsync(key, effective, includeSecrets, dependencies);
        return (resolved, dependencies);
    }

    private Task Set(string project, string key, KeyValueProtection protection, string value)
    {
        return KeyValues.SetAsync(Org, project, View, key, new KeyValueWrite { Protection = protection, Value = value }, Admin);
    }

    private async Task<string> KeyValueETagAsync(string project, string key)
    {
        return await ETagAsync($"kv.{key}", PartitionKeys.GetKeyValues(project, View));
    }

    private async Task<string> ETagAsync(string id, string partitionKey)
    {
        using var response = await Container.ReadItemStreamAsync(id, new PartitionKey(partitionKey));
        response.IsSuccessStatusCode.Should().BeTrue($"the item {id} exists");
        return response.Headers.ETag;
    }

    private async Task<FullKey> CreateResponsibilityAsync(string project, string content)
    {
        var annotationKey = AnnotationKey.CreateResponsibility("deps");
        await Annotations.CreateAnnotationAsync(Org, new Responsibility
        {
            AnnotationKey = annotationKey,
            AnnotationType = AnnotationType.Responsibility,
            Name = "deps",
            ProjectName = project,
            ViewName = View,
        });

        var key = FullKey.Create(annotationKey, Org, project, View);
        await Configs.CreateOrUpdateConfigurationAsync(key, JObject.Parse(content), AuditActor.System);
        return key;
    }

    // A usage (partitioned by its responsibility) with values, and its subject (partitioned by itself) with an owner.
    private async Task<(FullKey Usage, FullKey Subject)> CreateUsageAsync(string project, string content)
    {
        await Annotations.CreateAnnotationsFromStringAsync(Org, project, View, ["payments.billing"], AnnotationServiceTestExtensions.TestActor);
        var usageKey = FullKey.Create(AnnotationKey.CreateUsage("payments", "billing"), Org, project, View);
        var subjectKey = FullKey.Create(AnnotationKey.CreateSubject("payments"), Org, project, View);

        var usage = (await Annotations.GetAnnotationAsync(usageKey))!;
        await Annotations.UpdateAnnotationAsync(Org, usage with { Values = new Dictionary<string, object> { ["title"] = "usage title" } });
        var subject = (await Annotations.GetAnnotationAsync(subjectKey))!;
        await Annotations.UpdateAnnotationAsync(Org, subject with { Values = new Dictionary<string, object> { ["owner"] = "team-a" } });

        await Configs.CreateOrUpdateConfigurationAsync(usageKey, JObject.Parse(content), AuditActor.System);
        return (usageKey, subjectKey);
    }
}
