using System;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.Entities.Configurations;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

public class CosmosConfigurationTemplateServiceTests(Startup startup)
    : BaseTestsClass(startup)
{
    // every test works in its own project, templates of other tests never interfere
    private const string ProjectPrefix = "tpl-tests";
    private const string View = Constants.DefaultViewName;
    private const string OtherView = "other";

    // keep in sync with ConfigurationCacheInvalidator.MaxBatchOperations
    private const int ConfigurationCacheInvalidatorLimit = 100;

    private IConfigurationTemplateService Templates => Context.Services.GetRequiredService<IConfigurationTemplateService>();

    private IConfigurationService Configs => Context.Services.GetRequiredService<IConfigurationService>();

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    [Fact]
    public async Task NonExistingTemplate()
    {
        var project = $"{ProjectPrefix}-none";

        var template = await Templates.GetTemplateAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Execution);
        var versions = await Templates.GetTemplateVersionsAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Execution);
        var deleted = await Templates.DeleteTemplateAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Execution);

        template.Should().BeNull();
        versions.Should().BeEmpty();
        deleted.Should().BeFalse();
    }

    [Fact]
    public async Task VersioningOfTemplate()
    {
        var project = $"{ProjectPrefix}-versioning";
        var org = TestConstants.Organization;
        const string type = AnnotationTypeCodes.Execution;

        var content = JObject.Parse("""
                                    {
                                        "name": "versioning",
                                        "integer": 30,
                                        "decimal": 42.2
                                    }
                                    """);
        var version1 = await Templates.CreateOrUpdateTemplateAsync(org, project, View, type, content, "author-1");

        content.Add("array", JArray.Parse("""
                                          [
                                              "text",
                                              42,
                                              { "isObject" : true }
                                          ]
                                          """));
        var version2 = await Templates.CreateOrUpdateTemplateAsync(org, project, View, type, content, "author-2");

        var version3 = await Templates.CreateOrUpdateTemplateAsync(
            org,
            project,
            View,
            type,
            JObject.Parse("""{ "$remove": [ "$.*" ] }"""),
            "author-3");

        version1.Version.Should().Be(1);
        version2.Version.Should().Be(2);
        version3.Version.Should().Be(3);
        version3.AnnotationType.Should().Be(type);

        var versions = await Templates.GetTemplateVersionsAsync(org, project, View, type);
        versions.Select(v => v.Version).Should().Equal(1u, 2u, 3u);
        versions.Select(v => v.Author).Should().Equal("author-1", "author-2", "author-3");
        versions.Last().ExpirationTime.Should().Be(DateTimeOffset.MaxValue);
        versions.First().ExpirationTime.Should().BeAfter(DateTimeOffset.UtcNow.AddDays(300));

        (await Templates.GetTemplateVersionContentAsync(org, project, View, type, 1))!.Content.Should().HaveCount(3);
        (await Templates.GetTemplateVersionContentAsync(org, project, View, type, 2))!.Content.Should().HaveCount(4);
        (await Templates.GetTemplateVersionContentAsync(org, project, View, type, 3))!.Content.Should().BeEmpty();
        (await Templates.GetTemplateVersionContentAsync(org, project, View, type, 4)).Should().BeNull();

        var changes1 = await Templates.GetTemplateVersionChangesAsync(org, project, View, type, 1);
        var changes2 = await Templates.GetTemplateVersionChangesAsync(org, project, View, type, 2);
        var changes3 = await Templates.GetTemplateVersionChangesAsync(org, project, View, type, 3);

        changes1.Stats.Additions.Should().Be(5);
        changes1.Stats.Deletions.Should().Be(0);

        changes2.Stats.Additions.Should().Be(8);
        changes2.Stats.Deletions.Should().Be(1);
        changes2.Stats.Unchanged.Should().Be(4);

        changes3.Stats.Additions.Should().Be(0);
        changes3.Stats.Deletions.Should().Be(12);

        var diff1To3 = await Templates.GetTemplateVersionChangesAsync(org, project, View, type, 1, 3);
        diff1To3.Stats.Deletions.Should().Be(5);
        diff1To3.Stats.Additions.Should().Be(0);

        var unknownVersion = () => Templates.GetTemplateVersionChangesAsync(org, project, View, type, 7);
        await unknownVersion.Should().ThrowAsync<InvalidOperationException>();

        var changes0 = await Templates.GetTemplateVersionChangesAsync(org, project, View, type, 0);
        changes0.Stats.Additions.Should().Be(0);
        changes0.Stats.Deletions.Should().Be(0);
    }

    [Fact]
    public async Task UpdateWithSameContent_DoesNotCreateVersion()
    {
        var project = $"{ProjectPrefix}-same";
        var org = TestConstants.Organization;
        var content = JObject.Parse("""{ "retries": 3 }""");

        await Templates.CreateOrUpdateTemplateAsync(org, project, View, AnnotationTypeCodes.Usage, content, "system");
        var second = await Templates.CreateOrUpdateTemplateAsync(org, project, View, AnnotationTypeCodes.Usage, content, "someone-else");

        second.Version.Should().Be(1);
        second.Author.Should().Be("system");
        (await Templates.GetTemplateVersionsAsync(org, project, View, AnnotationTypeCodes.Usage)).Should().HaveCount(1);
    }

    [Fact]
    public async Task TypeCode_IsCaseInsensitive()
    {
        var project = $"{ProjectPrefix}-case";
        var org = TestConstants.Organization;

        await Templates.CreateOrUpdateTemplateAsync(org, project, View, "EXE", JObject.Parse("""{ "retries": 3 }"""), "system");
        var template = await Templates.GetTemplateAsync(org, project, View, AnnotationTypeCodes.Execution);

        template.Should().NotBeNull();
        template!.AnnotationType.Should().Be(AnnotationTypeCodes.Execution);
    }

    [Fact]
    public async Task UnknownTypeCode_Throws()
    {
        var act = () => Templates.GetTemplateAsync(TestConstants.Organization, $"{ProjectPrefix}-unknown", View, "xyz");
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task PatchTemplate_CreatesVersionHistory()
    {
        var project = $"{ProjectPrefix}-patch";
        var org = TestConstants.Organization;
        const string type = AnnotationTypeCodes.Context;

        await Templates.CreateOrUpdateTemplateAsync(org, project, View, type, JObject.Parse("""{ "retries": 3, "owner": "platform" }"""), "system");
        var patched = await Templates.PatchTemplateAsync(
            org,
            project,
            View,
            type,
            JArray.Parse("""[ { "op": "replace", "path": "/retries", "value": 5 }, { "op": "remove", "path": "/owner" } ]"""),
            "patcher");

        patched.Version.Should().Be(2);
        patched.Author.Should().Be("patcher");
        patched.Content.Should().HaveCount(1);
        patched.Content["retries"]!.Value<int>().Should().Be(5);

        var version1 = await Templates.GetTemplateVersionContentAsync(org, project, View, type, 1);
        version1!.Content["owner"]!.Value<string>().Should().Be("platform");
    }

    [Fact]
    public async Task PatchTemplate_Missing_Throws()
    {
        var act = () => Templates.PatchTemplateAsync(
            TestConstants.Organization,
            $"{ProjectPrefix}-patch-missing",
            View,
            AnnotationTypeCodes.Execution,
            JArray.Parse("""[ { "op": "add", "path": "/retries", "value": 5 } ]"""),
            "patcher");

        await act.Should().ThrowAsync<TemplateNotFoundException>();
    }

    [Fact]
    public async Task DeleteThenCreate_ContinuesVersionNumbering()
    {
        var project = $"{ProjectPrefix}-delete";
        var org = TestConstants.Organization;
        const string type = AnnotationTypeCodes.Unit;

        await Templates.CreateOrUpdateTemplateAsync(org, project, View, type, JObject.Parse("""{ "a": 1 }"""), "system");
        await Templates.CreateOrUpdateTemplateAsync(org, project, View, type, JObject.Parse("""{ "b": 2 }"""), "system");
        var deleted = await Templates.DeleteTemplateAsync(org, project, View, type);
        var afterDelete = await Templates.GetTemplateAsync(org, project, View, type);
        var recreated = await Templates.CreateOrUpdateTemplateAsync(org, project, View, type, JObject.Parse("""{ "c": 3 }"""), "system");

        deleted.Should().BeTrue();
        afterDelete.Should().BeNull();
        recreated.Version.Should().Be(3);
        recreated.Content.Should().HaveCount(1);

        var versions = await Templates.GetTemplateVersionsAsync(org, project, View, type);
        versions.Select(v => v.Version).Should().Equal(1u, 2u, 3u);
        (await Templates.GetTemplateVersionContentAsync(org, project, View, type, 2))!.Content.Should().HaveCount(2);
    }

    [Fact]
    public async Task ConcurrentTemplateWrites_ProduceConsecutiveVersions()
    {
        var project = $"{ProjectPrefix}-concurrent";
        var org = TestConstants.Organization;
        const string type = AnnotationTypeCodes.Responsibility;

        var writes = Enumerable.Range(1, 3)
            .Select(i => Templates.CreateOrUpdateTemplateAsync(org, project, View, type, JObject.Parse($$"""{ "writer{{i}}": {{i}} }"""), $"writer-{i}"));
        await Task.WhenAll(writes);

        var versions = await Templates.GetTemplateVersionsAsync(org, project, View, type);
        var current = await Templates.GetTemplateAsync(org, project, View, type);

        versions.Select(v => v.Version).Should().Equal(1u, 2u, 3u);
        current!.Content.Should().HaveCount(3);
    }

    [Fact]
    public async Task Template_AppliesToAllPartitionsInView()
    {
        var project = $"{ProjectPrefix}-scope";
        var org = TestConstants.Organization;

        var first = await CreateExecutionAsync(project, View, "customer", "billing", "prod");
        var second = await CreateExecutionAsync(project, View, "customer", "shipping", "prod");

        await Templates.CreateOrUpdateTemplateAsync(org, project, View, AnnotationTypeCodes.Execution, JObject.Parse("""{ "owner": "platform" }"""), "system");

        foreach (var key in new[] { first, second })
        {
            var configuration = await Configs.GetRawConfigurationAsync(key);
            configuration!.Content["owner"]!.Value<string>().Should().Be("platform");
        }
    }

    [Fact]
    public async Task Templates_AreIsolatedPerView()
    {
        var project = $"{ProjectPrefix}-views";
        var org = TestConstants.Organization;
        const string type = AnnotationTypeCodes.Execution;

        var defaultExecution = await CreateExecutionAsync(project, View, "customer", "billing", "prod");
        var otherExecution = await CreateExecutionAsync(project, OtherView, "customer", "billing", "prod");
        await Configs.CreateOrUpdateConfigurationAsync(otherExecution, JObject.Parse("""{ "retries": 5 }"""), "system");

        // a template only in the default view is not merged in the other view
        await Templates.CreateOrUpdateTemplateAsync(org, project, View, type, JObject.Parse("""{ "owner": "platform" }"""), "system");
        (await Configs.GetRawConfigurationAsync(defaultExecution))!.Content["owner"]!.Value<string>().Should().Be("platform");
        (await Configs.GetRawConfigurationAsync(otherExecution))!.Content.Should().NotContainKey("owner");
        (await Templates.GetTemplateAsync(org, project, OtherView, type)).Should().BeNull();

        // each view has its own content and its own version sequence
        await Templates.CreateOrUpdateTemplateAsync(org, project, View, type, JObject.Parse("""{ "tier": "gold" }"""), "system");
        var other = await Templates.CreateOrUpdateTemplateAsync(org, project, OtherView, type, JObject.Parse("""{ "owner": "team" }"""), "system");

        other.Version.Should().Be(1);
        (await Templates.GetTemplateVersionsAsync(org, project, View, type)).Should().HaveCount(2);
        (await Templates.GetTemplateVersionsAsync(org, project, OtherView, type)).Should().HaveCount(1);
        (await Configs.GetRawConfigurationAsync(otherExecution))!.Content["owner"]!.Value<string>().Should().Be("team");

        // a template write in one view leaves the cached calculations of the other view intact
        await Configs.GetRawConfigurationAsync(defaultExecution);
        (await HasCachedCalculationAsync(defaultExecution)).Should().BeTrue();
        await Templates.CreateOrUpdateTemplateAsync(org, project, OtherView, type, JObject.Parse("""{ "owner": "ops" }"""), "system");
        (await HasCachedCalculationAsync(defaultExecution)).Should().BeTrue();
        (await HasCachedCalculationAsync(otherExecution)).Should().BeFalse();

        var defaultConfiguration = await Configs.GetRawConfigurationAsync(defaultExecution);
        defaultConfiguration!.Content["owner"]!.Value<string>().Should().Be("platform");
        defaultConfiguration.Content["tier"]!.Value<string>().Should().Be("gold");
    }

    [Fact]
    public async Task TemplateMergeOrder_StoredContentWins()
    {
        var project = $"{ProjectPrefix}-merge";
        var org = TestConstants.Organization;
        var execution = await CreateExecutionAsync(project, Constants.DefaultViewName, "northwind", "invoicing", "production");
        var responsibility = FullKey.Create(execution.Annotation.GetResponsibilityKey(), execution);

        await Configs.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "currency": "EUR", "retries": 1, "features": ["vat"] }"""), "system");
        await Templates.CreateOrUpdateTemplateAsync(org, project, View, AnnotationTypeCodes.Execution, JObject.Parse("""{ "retries": 3, "features": ["audit"], "owner": "platform" }"""), "system");
        await Configs.CreateOrUpdateConfigurationAsync(execution, JObject.Parse("""{ "retries": 5, "features": ["vip"] }"""), "system");

        var configuration = await Configs.GetRawConfigurationAsync(execution);

        configuration!.Content["currency"]!.Value<string>().Should().Be("EUR");
        configuration.Content["retries"]!.Value<int>().Should().Be(5);
        configuration.Content["owner"]!.Value<string>().Should().Be("platform");
        configuration.Content["features"]!.Values<string>().Should().Equal("vat", "audit", "vip");
    }

    [Fact]
    public async Task TemplateWrite_InvalidatesCalculatedConfiguration()
    {
        var project = $"{ProjectPrefix}-invalidate";
        var org = TestConstants.Organization;
        var execution = await CreateExecutionAsync(project, Constants.DefaultViewName, "customer", "billing", "prod");
        await Configs.CreateOrUpdateConfigurationAsync(execution, JObject.Parse("""{ "retries": 5 }"""), "system");

        await Templates.CreateOrUpdateTemplateAsync(org, project, View, AnnotationTypeCodes.Execution, JObject.Parse("""{ "owner": "platform" }"""), "system");
        var first = await Configs.GetRawConfigurationAsync(execution);
        (await HasCachedCalculationAsync(execution)).Should().BeTrue();

        await Templates.CreateOrUpdateTemplateAsync(org, project, View, AnnotationTypeCodes.Execution, JObject.Parse("""{ "owner": "team" }"""), "system");
        (await HasCachedCalculationAsync(execution)).Should().BeFalse();
        var second = await Configs.GetRawConfigurationAsync(execution);

        await Templates.DeleteTemplateAsync(org, project, View, AnnotationTypeCodes.Execution);
        var third = await Configs.GetRawConfigurationAsync(execution);

        first!.Content["owner"]!.Value<string>().Should().Be("platform");
        second!.Content["owner"]!.Value<string>().Should().Be("team");
        second.Hash.Should().NotBe(first.Hash);
        third!.Content.Should().NotContainKey("owner");
        third.Content["retries"]!.Value<int>().Should().Be(5);
    }

    [Fact]
    public async Task SubjectTemplateWrite_InvalidatesDescendantsAcrossPartitions()
    {
        var project = $"{ProjectPrefix}-subject";
        var org = TestConstants.Organization;
        var unitOfExecution = await CreateUnitOfExecutionAsync(project, "customer", "billing", "prod", "worker");
        var usage = FullKey.Create(unitOfExecution.Annotation.GetUsageKey(), unitOfExecution);
        var execution = FullKey.Create(unitOfExecution.Annotation.GetExecutionKey(), unitOfExecution);

        await Templates.CreateOrUpdateTemplateAsync(org, project, View, AnnotationTypeCodes.Subject, JObject.Parse("""{ "tier": "gold" }"""), "system");
        foreach (var key in new[] { usage, execution, unitOfExecution })
        {
            (await Configs.GetRawConfigurationAsync(key))!.Content["tier"]!.Value<string>().Should().Be("gold");
        }

        await Templates.CreateOrUpdateTemplateAsync(org, project, View, AnnotationTypeCodes.Subject, JObject.Parse("""{ "tier": "silver" }"""), "system");
        foreach (var key in new[] { usage, execution, unitOfExecution })
        {
            (await Configs.GetRawConfigurationAsync(key))!.Content["tier"]!.Value<string>().Should().Be("silver");
        }
    }

    [Fact]
    public async Task TemplateWrite_DoesNotInvalidateUnrelatedTypes()
    {
        var project = $"{ProjectPrefix}-unrelated";
        var org = TestConstants.Organization;
        var execution = await CreateExecutionAsync(project, Constants.DefaultViewName, "customer", "billing", "prod");
        var responsibility = FullKey.Create(execution.Annotation.GetResponsibilityKey(), execution);
        var subject = FullKey.Create(execution.Annotation.GetSubjectKey(), execution);

        await Configs.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "level": "responsibility" }"""), "system");
        await Configs.CreateOrUpdateConfigurationAsync(subject, JObject.Parse("""{ "level": "subject" }"""), "system");
        await Configs.GetRawConfigurationAsync(responsibility);
        await Configs.GetRawConfigurationAsync(subject);
        await Configs.GetRawConfigurationAsync(execution);

        await Templates.CreateOrUpdateTemplateAsync(org, project, View, AnnotationTypeCodes.Execution, JObject.Parse("""{ "owner": "platform" }"""), "system");

        (await HasCachedCalculationAsync(responsibility)).Should().BeTrue();
        (await HasCachedCalculationAsync(subject)).Should().BeTrue();
        (await HasCachedCalculationAsync(execution)).Should().BeFalse();
    }

    [Fact]
    public async Task Invalidation_MoreThan100ConfigurationsInPartition()
    {
        const int unitCount = ConfigurationCacheInvalidatorLimit + 5;
        var project = $"{ProjectPrefix}-batch";
        var org = TestConstants.Organization;
        var responsibilityKey = AnnotationKey.CreateResponsibility("big");
        var units = Enumerable.Range(0, unitCount)
            .Select(i => FullKey.Create(AnnotationKey.CreateUnit("big", $"unit{i:000}"), org, project, Constants.DefaultViewName))
            .ToList();

        foreach (var unit in units)
        {
            await Annotations.CreateAnnotationAsync(org, new Unit
            {
                AnnotationKey = unit.Annotation,
                AnnotationType = AnnotationType.Unit,
                Name = unit.Annotation.UnitName,
                ResponsibilityKey = responsibilityKey,
                ResponsibilityName = "big",
                UnitType = "event",
                UnitDefinition = "unknown",
                ProjectName = project,
                ViewName = Constants.DefaultViewName,
            });
        }

        await Templates.CreateOrUpdateTemplateAsync(org, project, View, AnnotationTypeCodes.Unit, JObject.Parse("""{ "size": "s" }"""), "system");
        foreach (var unit in units)
        {
            await Configs.GetRawConfigurationAsync(unit);
        }

        await Templates.CreateOrUpdateTemplateAsync(org, project, View, AnnotationTypeCodes.Unit, JObject.Parse("""{ "size": "xl" }"""), "system");

        foreach (var unit in units)
        {
            (await Configs.GetRawConfigurationAsync(unit))!.Content["size"]!.Value<string>().Should().Be("xl");
        }
    }

    [Fact]
    public async Task LegacyTemplateInAnnotationPartition_IsIgnored()
    {
        var project = $"{ProjectPrefix}-legacy";
        var execution = await CreateExecutionAsync(project, Constants.DefaultViewName, "customer", "billing", "prod");
        await Configs.CreateOrUpdateConfigurationAsync(execution, JObject.Parse("""{ "retries": 5 }"""), "system");

        var partitionKey = PartitionKeys.GetResponsibility(project, "billing");
        await GetContainer().CreateItemAsync(
            new GenerateTemplateEntity
            {
                Id = $"{Constants.DefaultViewName}.gen.exe",
                PartitionKey = partitionKey,
                ProjectName = project,
                ViewName = Constants.DefaultViewName,
                AnnotationKey = "exe",
                Name = "exe",
                Author = "legacy",
                Content = JObject.Parse("""{ "legacy": true }"""),
            },
            new PartitionKey(partitionKey));

        var configuration = await Configs.GetRawConfigurationAsync(execution);

        configuration!.Content.Should().NotContainKey("legacy");
    }

    [Fact]
    public async Task CommittedChangeWithPendingInvalidation_IsCompletedOnRepeatedRequest()
    {
        var project = $"{ProjectPrefix}-pending-update";
        var org = TestConstants.Organization;
        const string type = AnnotationTypeCodes.Execution;
        var execution = await CreateExecutionAsync(project, View, "customer", "billing", "prod");

        await Templates.CreateOrUpdateTemplateAsync(org, project, View, type, JObject.Parse("""{ "owner": "platform" }"""), "system");
        await Configs.GetRawConfigurationAsync(execution);

        // simulate a committed template change whose invalidation failed afterwards
        await ReplaceRawItemAsync(project, $"{View}.gen.{type}", item => item["Content"]!["owner"] = "team");
        await ReplaceRawItemAsync(project, $"{View}.gns.{type}", item => item["IsInvalidationPending"] = true);
        (await Configs.GetRawConfigurationAsync(execution))!.Content["owner"]!.Value<string>().Should().Be("platform");

        // the repeated request has no change to write, but completes the invalidation
        var repeated = await Templates.CreateOrUpdateTemplateAsync(org, project, View, type, JObject.Parse("""{ "owner": "team" }"""), "system");

        repeated.Version.Should().Be(1);
        (await HasCachedCalculationAsync(execution)).Should().BeFalse();
        (await Configs.GetRawConfigurationAsync(execution))!.Content["owner"]!.Value<string>().Should().Be("team");
        (await ReadRawItemAsync(project, $"{View}.gns.{type}"))["IsInvalidationPending"]!.Value<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task CommittedDeletionWithPendingInvalidation_IsCompletedOnRepeatedDelete()
    {
        var project = $"{ProjectPrefix}-pending-delete";
        var org = TestConstants.Organization;
        const string type = AnnotationTypeCodes.Execution;
        var execution = await CreateExecutionAsync(project, View, "customer", "billing", "prod");
        await Configs.CreateOrUpdateConfigurationAsync(execution, JObject.Parse("""{ "retries": 5 }"""), "system");

        await Templates.CreateOrUpdateTemplateAsync(org, project, View, type, JObject.Parse("""{ "owner": "platform" }"""), "system");
        await Configs.GetRawConfigurationAsync(execution);

        // simulate a committed deletion whose invalidation failed afterwards
        await GetContainer().DeleteItemAsync<JObject>(
            $"{View}.gen.{type}",
            PartitionKeys.GetCosmosTemplate(project));
        await ReplaceRawItemAsync(project, $"{View}.gns.{type}", item => item["IsInvalidationPending"] = true);

        var deleted = await Templates.DeleteTemplateAsync(org, project, View, type);
        var configuration = await Configs.GetRawConfigurationAsync(execution);

        deleted.Should().BeFalse();
        configuration!.Content.Should().NotContainKey("owner");
        configuration.Content["retries"]!.Value<int>().Should().Be(5);
    }

    [Fact]
    public async Task DeleteThenCreate_AfterHistoryExpiration_DoesNotReuseVersion()
    {
        var project = $"{ProjectPrefix}-expired-history";
        var org = TestConstants.Organization;
        const string type = AnnotationTypeCodes.Unit;

        await Templates.CreateOrUpdateTemplateAsync(org, project, View, type, JObject.Parse("""{ "a": 1 }"""), "system");
        await Templates.CreateOrUpdateTemplateAsync(org, project, View, type, JObject.Parse("""{ "b": 2 }"""), "system");
        await Templates.DeleteTemplateAsync(org, project, View, type);

        // simulate the expiration (ttl) of the whole history
        foreach (var version in new[] { 1, 2 })
        {
            await GetContainer().DeleteItemAsync<JObject>($"{View}.gnv.{type}.{version}", PartitionKeys.GetCosmosTemplate(project));
        }

        var recreated = await Templates.CreateOrUpdateTemplateAsync(org, project, View, type, JObject.Parse("""{ "c": 3 }"""), "system");
        var updated = await Templates.CreateOrUpdateTemplateAsync(org, project, View, type, JObject.Parse("""{ "d": 4 }"""), "system");

        recreated.Version.Should().Be(3);
        updated.Version.Should().Be(4);
    }

    private Container GetContainer()
    {
        return Context.Services
            .GetRequiredService<IContainerRepositoryProvider>()
            .GetOrganizationContainer(TestConstants.Organization)
            .Container;
    }

    private async Task<JObject> ReadRawItemAsync(string project, string id)
    {
        var response = await GetContainer().ReadItemAsync<JObject>(id, PartitionKeys.GetCosmosTemplate(project));
        return response.Resource;
    }

    private async Task ReplaceRawItemAsync(string project, string id, Action<JObject> change)
    {
        var item = await ReadRawItemAsync(project, id);
        change(item);
        await GetContainer().ReplaceItemAsync(item, id, PartitionKeys.GetCosmosTemplate(project));
    }

    private async Task<bool> HasCachedCalculationAsync(FullKey key)
    {
        var item = await GetContainer().ReadItemAsync<JObject>(
            $"{key.ViewName}.cnf.{key.Annotation}",
            new PartitionKey(key.GetPartitionKey()));

        return item.Resource["CalculatedContent"]?.Type == JTokenType.Object;
    }

    private async Task<FullKey> CreateExecutionAsync(string project, string view, string subjectName, string responsibilityName, string contextName)
    {
        var executionKey = AnnotationKey.CreateExecution(subjectName, responsibilityName, contextName);
        await Annotations.CreateAnnotationAsync(TestConstants.Organization, new Execution
        {
            AnnotationKey = executionKey,
            AnnotationType = AnnotationType.Execution,
            Name = contextName,
            SubjectKey = AnnotationKey.CreateSubject(subjectName),
            SubjectName = subjectName,
            ResponsibilityKey = AnnotationKey.CreateResponsibility(responsibilityName),
            ResponsibilityName = responsibilityName,
            ContextKey = AnnotationKey.CreateContext(subjectName, contextName),
            ContextName = contextName,
            UsageKey = AnnotationKey.CreateUsage(subjectName, responsibilityName),
            ProjectName = project,
            ViewName = view,
        });

        return FullKey.Create(executionKey, TestConstants.Organization, project, view);
    }

    private async Task<FullKey> CreateUnitOfExecutionAsync(string project, string subjectName, string responsibilityName, string contextName, string unitName)
    {
        var key = AnnotationKey.CreateUnitOfExecution(subjectName, responsibilityName, contextName, unitName);
        await Annotations.CreateAnnotationAsync(TestConstants.Organization, new UnitOfExecution
        {
            AnnotationKey = key,
            AnnotationType = AnnotationType.UnitOfExecution,
            Name = unitName,
            SubjectKey = AnnotationKey.CreateSubject(subjectName),
            SubjectName = subjectName,
            ResponsibilityKey = AnnotationKey.CreateResponsibility(responsibilityName),
            ResponsibilityName = responsibilityName,
            ContextKey = AnnotationKey.CreateContext(subjectName, contextName),
            ContextName = contextName,
            UnitKey = AnnotationKey.CreateUnit(responsibilityName, unitName),
            UnitName = unitName,
            UsageKey = AnnotationKey.CreateUsage(subjectName, responsibilityName),
            ExecutionKey = AnnotationKey.CreateExecution(subjectName, responsibilityName, contextName),
            ProjectName = project,
            ViewName = Constants.DefaultViewName,
        });

        return FullKey.Create(key, TestConstants.Organization, project, Constants.DefaultViewName);
    }
}
