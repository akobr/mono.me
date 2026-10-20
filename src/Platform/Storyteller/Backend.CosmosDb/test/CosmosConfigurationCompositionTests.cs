using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The composition of the effective configuration against the emulator. The equivalence with the Effective read
// (GetRawConfigurationAsync) is the acceptance gate.
public class CosmosConfigurationCompositionTests(Startup startup)
    : BaseTestsClass(startup)
{
    // every test works in its own project, templates of other tests never interfere
    private const string ProjectPrefix = "cmp-tests";
    private const string View = Constants.DefaultViewName;

    private IConfigurationService Configs => Context.Services.GetRequiredService<IConfigurationService>();

    private IConfigurationTemplateService Templates => Context.Services.GetRequiredService<IConfigurationTemplateService>();

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    [Fact]
    public async Task SingleChain_EqualsTheEffectiveRead()
    {
        var project = $"{ProjectPrefix}-chain";
        var unit = await CreateUnitAsync(project, "billing", "export");
        var responsibility = FullKey.Create(unit.Annotation.GetResponsibilityKey(), unit);

        await Configs.CreateOrUpdateConfigurationAsync(
            responsibility,
            JObject.Parse("""{ "retries": 1, "database": { "host": "a", "port": 1 }, "tags": [ "x" ] }"""),
            AuditActor.System);
        await Configs.CreateOrUpdateConfigurationAsync(
            unit,
            JObject.Parse("""{ "schedule": "0 * * * *", "database": { "host": "b" }, "tags": [ "y", "x" ], "retries": 9 }"""),
            AuditActor.System);

        var composition = await AssertEquivalentAsync(unit);

        composition.Content.ToString(Formatting.None).Should().Be(
            """{"retries":9,"database":{"host":"b","port":1},"tags":["x","y"],"schedule":"0 * * * *"}""");
        Nodes(composition).Should().Equal(("rst.billing", 1, 1), ("unt.billing.export", null, 0));
        Steps(composition).Should().Equal((1, "configuration:rst.billing", 0), (2, "configuration:unt.billing.export", 1));
        Origins(composition).Should().Equal(
            ("/retries", 2),
            ("/database", 1),
            ("/database/host", 2),
            ("/database/port", 1),
            ("/tags", 1),
            ("/tags/0", 1),
            ("/tags/1", 2),
            ("/schedule", 2));
    }

    [Fact]
    public async Task Execution_SubjectThroughBothBranches_EqualsTheEffectiveRead()
    {
        var project = $"{ProjectPrefix}-execution";
        var execution = await CreateExecutionAsync(project, "northwind", "invoicing", "production");
        var subject = FullKey.Create(execution.Annotation.GetSubjectKey(), execution);
        var usage = FullKey.Create(execution.Annotation.GetUsageKey(), execution);
        var context = FullKey.Create(execution.Annotation.GetContextKey(), execution);

        await Configs.CreateOrUpdateConfigurationAsync(subject, JObject.Parse("""{ "currency": "NOK", "residency": "norway" }"""), AuditActor.System);
        await Configs.CreateOrUpdateConfigurationAsync(usage, JObject.Parse("""{ "currency": "USD", "plan": "enterprise" }"""), AuditActor.System);
        await Configs.CreateOrUpdateConfigurationAsync(context, JObject.Parse("""{ "endpoint": "https://northwind.example/invoices" }"""), AuditActor.System);

        var composition = await AssertEquivalentAsync(execution);

        composition.AnnotationKey.Should().Be("exe.northwind.invoicing.production");
        composition.Content.ToString(Formatting.None).Should().Be(
            """{"currency":"NOK","residency":"norway","plan":"enterprise","endpoint":"https://northwind.example/invoices"}""");
        Nodes(composition).Should().Equal(
            ("rst.invoicing", 2, 2),
            ("sbt.northwind", 2, 2),
            ("usg.northwind.invoicing", 5, 1),
            ("sbt.northwind", 4, 2),
            ("cnt.northwind.production", 5, 1),
            ("exe.northwind.invoicing.production", null, 0));
        Steps(composition).Should().Equal(
            (1, "configuration:rst.invoicing", 0),
            (2, "configuration:sbt.northwind", 1),
            (3, "configuration:usg.northwind.invoicing", 2),
            (4, "configuration:sbt.northwind", 3),
            (5, "configuration:cnt.northwind.production", 4),
            (6, "configuration:exe.northwind.invoicing.production", 5));

        // currency comes back from the subject through the context, residency keeps the first application
        Origins(composition).Should().Equal(("/currency", 4), ("/residency", 2), ("/plan", 3), ("/endpoint", 5));

        composition.Sources.Select(source => (source.Id, source.Presence)).Should().Equal(
            ("configuration:rst.invoicing", ConfigurationCompositionSourcePresence.Missing),
            ("configuration:sbt.northwind", ConfigurationCompositionSourcePresence.Present),
            ("configuration:usg.northwind.invoicing", ConfigurationCompositionSourcePresence.Present),
            ("configuration:cnt.northwind.production", ConfigurationCompositionSourcePresence.Present),
            ("configuration:exe.northwind.invoicing.production", ConfigurationCompositionSourcePresence.Missing));
        composition.Sources.Should().OnlyContain(source => source.Kind == ConfigurationCompositionSourceKind.Configuration);
        composition.Sources.Select(source => source.AnnotationType).Should().Equal(
            AnnotationType.Responsibility,
            AnnotationType.Subject,
            AnnotationType.Usage,
            AnnotationType.Context,
            AnnotationType.Execution);

        var subjectSource = composition.Sources[1];
        subjectSource.AnnotationKey.Should().Be("sbt.northwind");
        subjectSource.Version.Should().Be(1);
        subjectSource.Modified!.By.Should().Be(AuditActor.System);
        subjectSource.Content!.ToString(Formatting.None).Should().Be("""{"currency":"NOK","residency":"norway"}""");
    }

    [Fact]
    public async Task UnitOfExecution_ResponsibilityThroughBothBranches_EqualsTheEffectiveRead()
    {
        var project = $"{ProjectPrefix}-unit-of-execution";
        var unitOfExecution = await CreateUnitOfExecutionAsync(project, "northwind", "invoicing", "production", "export");
        var responsibility = FullKey.Create(unitOfExecution.Annotation.GetResponsibilityKey(), unitOfExecution);
        var unit = FullKey.Create(unitOfExecution.Annotation.GetUnitKey(), unitOfExecution);

        await Configs.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "retries": 1 }"""), AuditActor.System);
        await Configs.CreateOrUpdateConfigurationAsync(unit, JObject.Parse("""{ "retries": 9, "schedule": "0 * * * *" }"""), AuditActor.System);
        await Configs.CreateOrUpdateConfigurationAsync(unitOfExecution, JObject.Parse("""{ "schedule": "0 2 * * *" }"""), AuditActor.System);

        var composition = await AssertEquivalentAsync(unitOfExecution);

        composition.Content.ToString(Formatting.None).Should().Be("""{"retries":1,"schedule":"0 2 * * *"}""");
        Nodes(composition).Should().Equal(
            ("rst.invoicing", 1, 2),
            ("unt.invoicing.export", 8, 1),
            ("rst.invoicing", 4, 3),
            ("sbt.northwind", 4, 3),
            ("usg.northwind.invoicing", 7, 2),
            ("sbt.northwind", 6, 3),
            ("cnt.northwind.production", 7, 2),
            ("exe.northwind.invoicing.production", 8, 1),
            ("uxe.northwind.invoicing.production.export", null, 0));
        Steps(composition).Select(step => step.Node).Should().Equal(0, 1, 2, 3, 4, 5, 6, 7, 8);
        Steps(composition).Select(step => step.SourceId).Should().Equal(
            "configuration:rst.invoicing",
            "configuration:unt.invoicing.export",
            "configuration:rst.invoicing",
            "configuration:sbt.northwind",
            "configuration:usg.northwind.invoicing",
            "configuration:sbt.northwind",
            "configuration:cnt.northwind.production",
            "configuration:exe.northwind.invoicing.production",
            "configuration:uxe.northwind.invoicing.production.export");
        composition.Sources.Should().HaveCount(7);

        // retries comes back from the responsibility through the execution, the unit's 9 is replaced
        Origins(composition).Should().Equal(("/retries", 3), ("/schedule", 9));
    }

    [Fact]
    public async Task TemplateAtAnAncestorLevel_EqualsTheEffectiveRead()
    {
        var project = $"{ProjectPrefix}-ancestor-template";
        var execution = await CreateExecutionAsync(project, "northwind", "invoicing", "production");
        var usage = FullKey.Create(execution.Annotation.GetUsageKey(), execution);

        await Templates.CreateOrUpdateTemplateAsync(
            TestConstants.Organization,
            project,
            View,
            AnnotationTypeCodes.Subject,
            JObject.Parse("""{ "tier": "gold", "features": [ "audit" ] }"""),
            AuditActor.System);
        await Configs.CreateOrUpdateConfigurationAsync(usage, JObject.Parse("""{ "tier": "silver", "features": [ "vip" ] }"""), AuditActor.System);

        var composition = await AssertEquivalentAsync(execution);

        // the subject template comes back through the context and replaces the usage's tier
        composition.Content.ToString(Formatting.None).Should().Be("""{"tier":"gold","features":["audit","vip"]}""");
        Steps(composition).Should().Equal(
            (1, "configuration:rst.invoicing", 0),
            (2, "template:sbt", 1),
            (3, "configuration:sbt.northwind", 1),
            (4, "configuration:usg.northwind.invoicing", 2),
            (5, "template:sbt", 3),
            (6, "configuration:sbt.northwind", 3),
            (7, "configuration:cnt.northwind.production", 4),
            (8, "configuration:exe.northwind.invoicing.production", 5));
        Origins(composition).Should().Equal(("/tier", 5), ("/features", 2), ("/features/0", 2), ("/features/1", 4));

        composition.Sources.Select(source => source.Id).Should().Equal(
            "configuration:rst.invoicing",
            "template:sbt",
            "configuration:sbt.northwind",
            "configuration:usg.northwind.invoicing",
            "configuration:cnt.northwind.production",
            "configuration:exe.northwind.invoicing.production");

        var template = composition.Sources[1];
        template.Kind.Should().Be(ConfigurationCompositionSourceKind.Template);
        template.AnnotationType.Should().Be(AnnotationType.Subject);
        template.AnnotationKey.Should().BeNull();
        template.Presence.Should().Be(ConfigurationCompositionSourcePresence.Present);
        template.Version.Should().Be(1);
        template.Modified!.By.Should().Be(AuditActor.System);
        template.Content!.ToString(Formatting.None).Should().Be("""{"tier":"gold","features":["audit"]}""");
    }

    [Fact]
    public async Task TemplatesForEveryType_TemplateStepBeforeTheConfigurationStepOfEachNode()
    {
        var project = $"{ProjectPrefix}-every-template";
        var execution = await CreateExecutionAsync(project, "northwind", "invoicing", "production");
        var responsibility = FullKey.Create(execution.Annotation.GetResponsibilityKey(), execution);
        var context = FullKey.Create(execution.Annotation.GetContextKey(), execution);

        foreach (var typeCode in new[] { AnnotationTypeCodes.Responsibility, AnnotationTypeCodes.Subject, AnnotationTypeCodes.Usage, AnnotationTypeCodes.Context, AnnotationTypeCodes.Execution })
        {
            await Templates.CreateOrUpdateTemplateAsync(
                TestConstants.Organization,
                project,
                View,
                typeCode,
                JObject.Parse($$"""{ "level": "{{typeCode}}", "{{typeCode}}": true, "list": [ "{{typeCode}}" ] }"""),
                AuditActor.System);
        }

        await Configs.CreateOrUpdateConfigurationAsync(responsibility, JObject.Parse("""{ "level": "rst-own", "nested": { "a": 1 } }"""), AuditActor.System);
        await Configs.CreateOrUpdateConfigurationAsync(context, JObject.Parse("""{ "nested": { "b": 2 }, "list": [ "cnt-own" ] }"""), AuditActor.System);

        var composition = await AssertEquivalentAsync(execution);

        Steps(composition).Should().Equal(
            (1, "template:rst", 0),
            (2, "configuration:rst.invoicing", 0),
            (3, "template:sbt", 1),
            (4, "configuration:sbt.northwind", 1),
            (5, "template:usg", 2),
            (6, "configuration:usg.northwind.invoicing", 2),
            (7, "template:sbt", 3),
            (8, "configuration:sbt.northwind", 3),
            (9, "template:cnt", 4),
            (10, "configuration:cnt.northwind.production", 4),
            (11, "template:exe", 5),
            (12, "configuration:exe.northwind.invoicing.production", 5));
        composition.Sources.Should().HaveCount(10);
        composition.Content["level"]!.Value<string>().Should().Be("exe");
        Origins(composition).Should().Contain(("/level", 11));
    }

    [Fact]
    public async Task ColdComposition_EqualsTheOneAfterTheEffectiveRead_AndWritesNothing()
    {
        var project = $"{ProjectPrefix}-cold";
        var execution = await CreateExecutionAsync(project, "northwind", "invoicing", "production");
        var responsibility = FullKey.Create(execution.Annotation.GetResponsibilityKey(), execution);
        var subject = FullKey.Create(execution.Annotation.GetSubjectKey(), execution);
        var usage = FullKey.Create(execution.Annotation.GetUsageKey(), execution);
        var context = FullKey.Create(execution.Annotation.GetContextKey(), execution);
        var keys = new[] { responsibility, subject, usage, context, execution };

        await Configs.CreateOrUpdateConfigurationAsync(subject, JObject.Parse("""{ "currency": "NOK" }"""), AuditActor.System);
        var before = await ReadItemTagsAsync(keys);

        var cold = await Configs.GetConfigurationCompositionAsync(execution);

        // no cache item is created and no stored item changes
        (await ReadItemTagsAsync(keys)).Should().Equal(before);
        before[usage.Annotation.ToString()].Should().BeNull();
        (await HasCachedCalculationAsync(subject)).Should().BeFalse();

        // the Effective read caches the calculations, with cache-only items for the ancestors without a configuration
        var effective = await Configs.GetRawConfigurationAsync(execution);
        (await HasConfigurationItemAsync(usage)).Should().BeTrue();
        (await HasConfigurationItemAsync(context)).Should().BeTrue();
        (await HasConfigurationItemAsync(execution)).Should().BeTrue();

        var warm = await Configs.GetConfigurationCompositionAsync(execution);

        JsonConvert.SerializeObject(warm).Should().Be(JsonConvert.SerializeObject(cold));
        warm!.Content.ToString(Formatting.None).Should().Be(effective!.Content.ToString(Formatting.None));
        warm.Hash.Should().Be(effective.Hash);

        var usageSource = warm.Sources.Single(source => source.Id == "configuration:usg.northwind.invoicing");
        usageSource.Presence.Should().Be(ConfigurationCompositionSourcePresence.Missing);
        usageSource.Version.Should().Be(0);
        usageSource.Modified.Should().BeNull();
        usageSource.Content.Should().BeNull();
    }

    [Fact]
    public async Task Presence_MissingEmptyAndPresent_AndNoStepForAMissingTemplate()
    {
        var project = $"{ProjectPrefix}-presence";
        var execution = await CreateExecutionAsync(project, "northwind", "invoicing", "production");
        var responsibility = FullKey.Create(execution.Annotation.GetResponsibilityKey(), execution);
        var subject = FullKey.Create(execution.Annotation.GetSubjectKey(), execution);
        var usage = FullKey.Create(execution.Annotation.GetUsageKey(), execution);

        await Configs.CreateOrUpdateConfigurationAsync(responsibility, new JObject(), AuditActor.Account("author"));
        await Configs.CreateOrUpdateConfigurationAsync(subject, JObject.Parse("""{ "a": 1 }"""), AuditActor.System);
        await Templates.CreateOrUpdateTemplateAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Usage, JObject.Parse("""{ "t": true }"""), AuditActor.System);

        // the Effective read of the usage leaves an item that holds only the cache (version 0, empty content)
        await Configs.GetRawConfigurationAsync(usage);
        var cacheOnly = await ReadItemAsync(usage);
        cacheOnly["Version"]!.Value<ulong>().Should().Be(0);
        ((JObject)cacheOnly["Content"]!).Should().BeEmpty();

        var composition = await AssertEquivalentAsync(usage);

        Nodes(composition).Should().Equal(("rst.invoicing", 2, 1), ("sbt.northwind", 2, 1), ("usg.northwind.invoicing", null, 0));
        Steps(composition).Should().Equal(
            (1, "configuration:rst.invoicing", 0),
            (2, "configuration:sbt.northwind", 1),
            (3, "template:usg", 2),
            (4, "configuration:usg.northwind.invoicing", 2));
        Origins(composition).Should().Equal(("/a", 2), ("/t", 3));

        var sources = composition.Sources.ToDictionary(source => source.Id);

        var empty = sources["configuration:rst.invoicing"];
        empty.Presence.Should().Be(ConfigurationCompositionSourcePresence.Empty);
        empty.Version.Should().Be(1);
        empty.Modified!.By.Id.Should().Be("author");
        empty.Content.Should().BeEmpty();

        var present = sources["configuration:sbt.northwind"];
        present.Presence.Should().Be(ConfigurationCompositionSourcePresence.Present);
        present.Version.Should().Be(1);

        var missing = sources["configuration:usg.northwind.invoicing"];
        missing.Presence.Should().Be(ConfigurationCompositionSourcePresence.Missing);
        missing.Version.Should().Be(0);
        missing.Modified.Should().BeNull();
        missing.Content.Should().BeNull();

        sources["template:usg"].Presence.Should().Be(ConfigurationCompositionSourcePresence.Present);
    }

    [Fact]
    public async Task DeletedOrUnknownAnnotation_ReturnsNull()
    {
        var project = $"{ProjectPrefix}-deleted";
        var configured = await CreateResponsibilityAsync(project, "configured");
        var plain = await CreateResponsibilityAsync(project, "plain");
        var unknown = FullKey.Create(AnnotationKey.CreateResponsibility("unknown"), TestConstants.Organization, project, View);

        await Configs.CreateOrUpdateConfigurationAsync(configured, JObject.Parse("""{ "a": 1 }"""), AuditActor.System);
        await Annotations.DeleteAnnotationAsync(configured);
        await Annotations.DeleteAnnotationAsync(plain);

        (await Configs.GetConfigurationCompositionAsync(configured)).Should().BeNull();
        (await Configs.GetConfigurationCompositionAsync(plain)).Should().BeNull();
        (await Configs.GetConfigurationCompositionAsync(unknown)).Should().BeNull();
        (await Configs.GetRawConfigurationAsync(configured)).Should().BeNull();
        (await Configs.GetRawConfigurationAsync(plain)).Should().BeNull();
        (await Configs.GetRawConfigurationAsync(unknown)).Should().BeNull();
    }

    [Fact]
    public async Task ActiveAnnotationWithoutItem_HasAMissingOwnStep()
    {
        var project = $"{ProjectPrefix}-active";
        var responsibility = await CreateResponsibilityAsync(project, "bare");

        var composition = await AssertEquivalentAsync(responsibility);

        composition.Content.Should().BeEmpty();
        composition.Hash.Should().BeNull();
        Nodes(composition).Should().Equal(("rst.bare", null, 0));
        Steps(composition).Should().Equal((1, "configuration:rst.bare", 0));
        composition.Sources.Should().ContainSingle().Which.Presence.Should().Be(ConfigurationCompositionSourcePresence.Missing);
        composition.Origins.Should().BeEmpty();
    }

    // The composition before the Effective read equals it (content with its property order, and hash), the replay of
    // the model gives the content, and the composition after the Effective read is the same.
    private async Task<ConfigurationComposition> AssertEquivalentAsync(FullKey key)
    {
        var composition = await Configs.GetConfigurationCompositionAsync(key);
        var effective = await Configs.GetRawConfigurationAsync(key);
        var cached = await Configs.GetRawConfigurationAsync(key);
        var after = await Configs.GetConfigurationCompositionAsync(key);

        composition.Should().NotBeNull();
        effective.Should().NotBeNull();
        var content = composition!.Content.ToString(Formatting.None);

        content.Should().Be(effective!.Content.ToString(Formatting.None));
        composition.Hash.Should().Be(effective.Hash);
        content.Should().Be(cached!.Content.ToString(Formatting.None));
        composition.Hash.Should().Be(cached.Hash);
        Replay(composition).ToString(Formatting.None).Should().Be(content);
        JsonConvert.SerializeObject(after).Should().Be(JsonConvert.SerializeObject(composition));

        composition.Nodes.Select(node => node.Index).Should().Equal(Enumerable.Range(0, composition.Nodes.Count));
        composition.Steps.Select(step => step.Order).Should().Equal(Enumerable.Range(1, composition.Steps.Count));
        composition.Nodes[^1].AnnotationKey.Should().Be(key.Annotation.ToString());
        return composition;
    }

    // The replay rule of the model: each node starts from an empty document, merges the results of the nodes merged
    // into it, then the documents of its own steps; the result of the last node is the content.
    private static JObject Replay(ConfigurationComposition composition)
    {
        var sources = composition.Sources.ToDictionary(source => source.Id);
        var results = new List<JObject>();

        foreach (var node in composition.Nodes)
        {
            var result = new JObject();

            foreach (var parent in composition.Nodes.Where(parent => parent.MergedInto == node.Index))
            {
                result.MergeInto(results[parent.Index]);
            }

            foreach (var step in composition.Steps.Where(step => step.Node == node.Index).OrderBy(step => step.Order))
            {
                if (sources[step.SourceId].Content is { } stepContent)
                {
                    result.MergeInto(stepContent);
                }
            }

            results.Add(result);
        }

        return results[^1];
    }

    private static IEnumerable<(string AnnotationKey, int? MergedInto, int Depth)> Nodes(ConfigurationComposition composition)
    {
        return composition.Nodes.Select(node => (node.AnnotationKey, node.MergedInto, node.Depth));
    }

    private static IEnumerable<(int Order, string SourceId, int Node)> Steps(ConfigurationComposition composition)
    {
        return composition.Steps.Select(step => (step.Order, step.SourceId, step.Node));
    }

    private static IEnumerable<(string Path, int Step)> Origins(ConfigurationComposition composition)
    {
        return composition.Origins.Select(origin => (origin.Path, origin.Step));
    }

    private Container GetContainer()
    {
        return Context.Services
            .GetRequiredService<IContainerRepositoryProvider>()
            .GetOrganizationContainer(TestConstants.Organization)
            .Container;
    }

    private async Task<JObject> ReadItemAsync(FullKey key)
    {
        var item = await GetContainer().ReadItemAsync<JObject>(
            $"{key.ViewName}.cnf.{key.Annotation}",
            new PartitionKey(key.GetPartitionKey()));

        return item.Resource;
    }

    private async Task<bool> HasConfigurationItemAsync(FullKey key)
    {
        using var response = await GetContainer().ReadItemStreamAsync(
            $"{key.ViewName}.cnf.{key.Annotation}",
            new PartitionKey(key.GetPartitionKey()));

        return response.IsSuccessStatusCode;
    }

    private async Task<bool> HasCachedCalculationAsync(FullKey key)
    {
        var item = await ReadItemAsync(key);
        return item["CalculatedContent"]?.Type == JTokenType.Object;
    }

    // The ETag of each configuration item, null for no item.
    private async Task<Dictionary<string, string>> ReadItemTagsAsync(IEnumerable<FullKey> keys)
    {
        var tags = new Dictionary<string, string>();

        foreach (var key in keys)
        {
            using var response = await GetContainer().ReadItemStreamAsync(
                $"{key.ViewName}.cnf.{key.Annotation}",
                new PartitionKey(key.GetPartitionKey()));

            tags[key.Annotation.ToString()] = response.IsSuccessStatusCode ? response.Headers.ETag : null;
        }

        return tags;
    }

    private async Task<FullKey> CreateResponsibilityAsync(string project, string name)
    {
        var key = AnnotationKey.CreateResponsibility(name);
        await Annotations.CreateAnnotationAsync(TestConstants.Organization, new Responsibility
        {
            AnnotationKey = key,
            AnnotationType = AnnotationType.Responsibility,
            Name = name,
            ProjectName = project,
            ViewName = View,
        });

        return FullKey.Create(key, TestConstants.Organization, project, View);
    }

    private async Task<FullKey> CreateUnitAsync(string project, string responsibilityName, string unitName)
    {
        var key = AnnotationKey.CreateUnit(responsibilityName, unitName);
        await Annotations.CreateAnnotationAsync(TestConstants.Organization, new Unit
        {
            AnnotationKey = key,
            AnnotationType = AnnotationType.Unit,
            Name = unitName,
            ResponsibilityKey = AnnotationKey.CreateResponsibility(responsibilityName),
            ResponsibilityName = responsibilityName,
            UnitType = "event",
            UnitDefinition = "unknown",
            ProjectName = project,
            ViewName = View,
        });

        return FullKey.Create(key, TestConstants.Organization, project, View);
    }

    private async Task<FullKey> CreateExecutionAsync(string project, string subjectName, string responsibilityName, string contextName)
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
            ViewName = View,
        });

        return FullKey.Create(executionKey, TestConstants.Organization, project, View);
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
            ViewName = View,
        });

        return FullKey.Create(key, TestConstants.Organization, project, View);
    }
}
