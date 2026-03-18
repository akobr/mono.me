using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

public class CosmosConfigurationServiceTests(Startup startup)
    : BaseTestsClass(startup)
{
    [Fact]
    public async Task NonExistingConfiguration()
    {
        var configs = Context.Services.GetRequiredService<IConfigurationService>();

        var annotationKey = AnnotationKey.CreateResponsibility("non-exist");
        var key = FullKey.Create(annotationKey, TestConstants.Organization, Constants.DefaultProjectName, Constants.DefaultViewName);
        var configuration = await configs.GetRawConfigurationAsync(key);

        configuration.Should().BeNull();
    }

    [Fact]
    public async Task EmptyConfiguration()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();
        var configs = Context.Services.GetRequiredService<IConfigurationService>();

        await annotations.CreateAnnotationAsync(TestConstants.Organization, new Responsibility
        {
            AnnotationKey = AnnotationKey.CreateResponsibility("empty"),
            AnnotationType = AnnotationType.Responsibility,
            Name = "empty",
            ProjectName = Constants.DefaultProjectName,
            ViewName = Constants.DefaultViewName,
        });

        var annotationKey = AnnotationKey.CreateResponsibility("empty");
        var key = FullKey.Create(annotationKey, TestConstants.Organization, Constants.DefaultProjectName, Constants.DefaultViewName);
        var configuration = await configs.GetRawConfigurationAsync(key);

        configuration.Should().NotBeNull();
        configuration.Should().BeEmpty();
    }

    [Fact]
    public async Task SimpleConfiguration()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();
        var configs = Context.Services.GetRequiredService<IConfigurationService>();

        var annotationKey = AnnotationKey.CreateResponsibility("simple");
        await annotations.CreateAnnotationAsync(TestConstants.Organization, new Responsibility
        {
            AnnotationKey = annotationKey,
            AnnotationType = AnnotationType.Responsibility,
            Name = "simple",
            ProjectName = Constants.DefaultProjectName,
            ViewName = Constants.DefaultViewName,
        });

        var configuration = JObject.Parse("""
                                                 {
                                                     "name": "John",
                                                     "age": 30,
                                                     "address": {
                                                         "street": "123 Main St",
                                                         "city": "Anytown"
                                                     }
                                                 }
                                                 """);

        var key = FullKey.Create(annotationKey, TestConstants.Organization, Constants.DefaultProjectName, Constants.DefaultViewName);
        await configs.CreateOrUpdateConfigurationAsync(key, configuration, "system");
        var hasContent = await configs.HasConfigurationContentAsync(key);
        var retrieveConfig = await configs.GetRawConfigurationAsync(key);

        hasContent.Should().BeTrue();
        retrieveConfig.Should().NotBeNull();
        retrieveConfig.Should().HaveCount(3);
        retrieveConfig.Should().Contain(pair => pair.Key == "name");
        retrieveConfig.Should().Contain(pair => pair.Key == "age");
        retrieveConfig.Should().Contain(pair => pair.Key == "address");
    }

    [Fact]
    public async Task ClearConfiguration()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();
        var configs = Context.Services.GetRequiredService<IConfigurationService>();

        var annotationKey = AnnotationKey.CreateSubject("clear");
        await annotations.CreateAnnotationAsync(TestConstants.Organization, new Subject
        {
            AnnotationKey = annotationKey,
            AnnotationType = AnnotationType.Subject,
            Name = "clear",
            ProjectName = Constants.DefaultProjectName,
            ViewName = Constants.DefaultViewName,
        });

        var configuration = JObject.Parse("""
                                                 {
                                                     "name": "clear",
                                                     "integer": 30,
                                                     "decimal": 42.2
                                                 }
                                                 """);

        var key = FullKey.Create(annotationKey, TestConstants.Organization, Constants.DefaultProjectName, Constants.DefaultViewName);
        await configs.CreateOrUpdateConfigurationAsync(key, configuration, "system");
        var hasContentBefore = await configs.HasConfigurationContentAsync(key);
        await configs.ClearConfigurationAsync(key);
        var hasContentAfter = await configs.HasConfigurationContentAsync(key);
        var retrieveConfig = await configs.GetRawConfigurationAsync(key);

        hasContentBefore.Should().BeTrue();
        hasContentAfter.Should().BeFalse();
        retrieveConfig.Should().NotBeNull();
        retrieveConfig.Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteConfiguration()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();
        var configs = Context.Services.GetRequiredService<IConfigurationService>();

        var annotationKey = AnnotationKey.CreateSubject("delete");
        await annotations.CreateAnnotationAsync(TestConstants.Organization, new Subject
        {
            AnnotationKey = annotationKey,
            AnnotationType = AnnotationType.Subject,
            Name = "delete",
            ProjectName = Constants.DefaultProjectName,
            ViewName = Constants.DefaultViewName,
        });

        var configuration = JObject.Parse("""
                                                 {
                                                   "reason": "delete this"
                                                 }
                                                 """);

        var key = FullKey.Create(annotationKey, TestConstants.Organization, Constants.DefaultProjectName, Constants.DefaultViewName);
        await configs.CreateOrUpdateConfigurationAsync(key, configuration, "system");
        var hasContentBefore = await configs.HasConfigurationContentAsync(key);
        await configs.DeleteAsync(key);
        await annotations.DeleteAnnotationAsync(key);
        var hasContentAfter = await configs.HasConfigurationContentAsync(key);
        var retrieveConfig = await configs.GetRawConfigurationAsync(key);

        hasContentBefore.Should().BeTrue();
        hasContentAfter.Should().BeFalse();
        retrieveConfig.Should().BeNull();
    }

    [Fact]
    public async Task DeleteConfigurationWithDescendants()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();
        var configs = Context.Services.GetRequiredService<IConfigurationService>();

        var subjectName = "descendants";
        var subjectAnnotationKey = AnnotationKey.CreateSubject(subjectName);
        await annotations.CreateAnnotationAsync(TestConstants.Organization, new Subject
        {
            AnnotationKey = subjectAnnotationKey,
            AnnotationType = AnnotationType.Subject,
            Name = subjectName,
            ProjectName = Constants.DefaultProjectName,
            ViewName = Constants.DefaultViewName,
        });

        await annotations.CreateAnnotationAsync(TestConstants.Organization, new Context
        {
            AnnotationKey = AnnotationKey.CreateContext(subjectName, "first"),
            AnnotationType = AnnotationType.Context,
            Name = "first",
            SubjectName = subjectName,
            SubjectKey = subjectAnnotationKey,
            ProjectName = Constants.DefaultProjectName,
            ViewName = Constants.DefaultViewName,
        });

        await annotations.CreateAnnotationAsync(TestConstants.Organization, new Context
        {
            AnnotationKey = AnnotationKey.CreateContext(subjectName, "second"),
            AnnotationType = AnnotationType.Context,
            Name = "second",
            SubjectName = subjectName,
            SubjectKey = subjectAnnotationKey,
            ProjectName = Constants.DefaultProjectName,
            ViewName = Constants.DefaultViewName,
        });

        await annotations.CreateAnnotationAsync(TestConstants.Organization, new Responsibility
        {
            AnnotationKey = AnnotationKey.CreateResponsibility("app1"),
            AnnotationType = AnnotationType.Responsibility,
            Name = "app1",
            ProjectName = Constants.DefaultProjectName,
            ViewName = Constants.DefaultViewName,
        });

        await annotations.CreateAnnotationAsync(TestConstants.Organization, new Responsibility
        {
            AnnotationKey = AnnotationKey.CreateResponsibility("app2"),
            AnnotationType = AnnotationType.Responsibility,
            Name = "app2",
            ProjectName = Constants.DefaultProjectName,
            ViewName = Constants.DefaultViewName,
        });

        await annotations.CreateAnnotationAsync(TestConstants.Organization, new Usage
        {
            AnnotationKey = AnnotationKey.CreateUsage(subjectName, "app1"),
            AnnotationType = AnnotationType.Usage,
            Name = "app1",
            SubjectKey = subjectAnnotationKey,
            SubjectName = subjectName,
            ResponsibilityKey = AnnotationKey.CreateResponsibility("app1"),
            ResponsibilityName = "app1",
            ProjectName = Constants.DefaultProjectName,
            ViewName = Constants.DefaultViewName,
        });

        await annotations.CreateAnnotationAsync(TestConstants.Organization, new Usage
        {
            AnnotationKey = AnnotationKey.CreateUsage(subjectName, "app2"),
            AnnotationType = AnnotationType.Usage,
            Name = "app2",
            SubjectKey = subjectAnnotationKey,
            SubjectName = subjectName,
            ResponsibilityKey = AnnotationKey.CreateResponsibility("app2"),
            ResponsibilityName = "app2",
            ProjectName = Constants.DefaultProjectName,
            ViewName = Constants.DefaultViewName,
        });

        var executionKey = FullKey.Create(
            AnnotationKey.CreateExecution(subjectName, "app1", "first"), TestConstants.Organization,
            Constants.DefaultProjectName,
            Constants.DefaultViewName);

        await annotations.CreateAnnotationAsync(TestConstants.Organization, new Execution
        {
            AnnotationKey = executionKey.Annotation,
            AnnotationType = AnnotationType.Execution,
            Name = "first",
            SubjectKey = subjectAnnotationKey,
            SubjectName = subjectName,
            ResponsibilityKey = AnnotationKey.CreateResponsibility("app1"),
            ResponsibilityName = "app1",
            ContextKey = AnnotationKey.CreateContext(subjectName, "first"),
            ContextName = "first",
            ProjectName = Constants.DefaultProjectName,
            ViewName = Constants.DefaultViewName,
        });

        await annotations.CreateAnnotationAsync(TestConstants.Organization, new Execution
        {
            AnnotationKey = AnnotationKey.CreateExecution(subjectName, "app1", "second"),
            AnnotationType = AnnotationType.Execution,
            Name = "second",
            SubjectKey = subjectAnnotationKey,
            SubjectName = subjectName,
            ResponsibilityKey = AnnotationKey.CreateResponsibility("app2"),
            ResponsibilityName = "app2",
            ContextKey = AnnotationKey.CreateContext(subjectName, "second"),
            ContextName = "second",
            ProjectName = Constants.DefaultProjectName,
            ViewName = Constants.DefaultViewName,
        });

        var configuration = JObject.Parse("""
                                                 {
                                                   "reason": "descendants test"
                                                 }
                                                 """);

        var subjectKey = FullKey.Create(subjectAnnotationKey, TestConstants.Organization, Constants.DefaultProjectName, Constants.DefaultViewName);
        await configs.CreateOrUpdateConfigurationAsync(subjectKey, configuration, "system");
        var executionConfigBeforeDelete = await configs.GetRawConfigurationAsync(executionKey);

        // TODO: [P3] replace it with just calling config service
        await annotations.DeleteAnnotationAsync(subjectKey); // this will delete configuration with descendants
        var executionConfigAfterDelete = await configs.GetRawConfigurationAsync(executionKey);

        executionConfigBeforeDelete.Should().NotBeEmpty();
        executionConfigBeforeDelete.Should().ContainKey("reason");
        executionConfigAfterDelete.Should().BeNull();
    }

    [Fact]
    public async Task VersioningOfConfiguration()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();
        var configs = Context.Services.GetRequiredService<IConfigurationService>();

        var annotationKey = AnnotationKey.CreateResponsibility("versioning");
        await annotations.CreateAnnotationAsync(TestConstants.Organization, new Responsibility
        {
            AnnotationKey = annotationKey,
            AnnotationType = AnnotationType.Responsibility,
            Name = "versioning",
            ProjectName = Constants.DefaultProjectName,
            ViewName = Constants.DefaultViewName,
        });

        var configuration = JObject.Parse("""
                                                 {
                                                     "name": "versioning",
                                                     "integer": 30,
                                                     "decimal": 42.2
                                                 }
                                                 """);

        var key = FullKey.Create(annotationKey, TestConstants.Organization, Constants.DefaultProjectName, Constants.DefaultViewName);
        await configs.CreateOrUpdateConfigurationAsync(key, configuration, "system"); // version 1

        configuration.Add("array", JArray.Parse("""
                                                [
                                                    "text",
                                                    42,
                                                    { "isObject" : true }
                                                ]
                                                """));

        await configs.CreateOrUpdateConfigurationAsync(key, configuration, "system"); // version 2
        configuration = new JObject();
        await configs.CreateOrUpdateConfigurationAsync(key, configuration, "system"); // version 3 (current)

        var versions = await configs.GetConfigurationVersionsAsync(key);
        var version1 = await configs.GetConfigurationVersionContentAsync(key, 1);
        var version2 = await configs.GetConfigurationVersionContentAsync(key, 2);
        var version3 = await configs.GetConfigurationVersionContentAsync(key, 3);
        var currentConfiguration = await configs.GetRawConfigurationAsync(key);

        var changes1 = await configs.GetConfigurationVersionChangesAsync(key, 1);
        var changes2 = await configs.GetConfigurationVersionChangesAsync(key, 2);
        var changes3 = await configs.GetConfigurationVersionChangesAsync(key, 3);

        versions.Should().HaveCount(3);
        version1.Should().HaveCount(3);
        version2.Should().HaveCount(4);
        version3.Should().HaveCount(0);

        changes1.Count(line => line.StartsWith("+ ")).Should().Be(5);
        changes1.Count(line => line.StartsWith("- ")).Should().Be(0);
        changes1.Count(line => line.StartsWith("  ")).Should().Be(0);

        changes2.Count(line => line.StartsWith("+ ")).Should().Be(8);
        changes2.Count(line => line.StartsWith("- ")).Should().Be(1);
        changes2.Count(line => line.StartsWith("  ")).Should().Be(4);

        changes3.Count(line => line.StartsWith("+ ")).Should().Be(0);
        changes3.Count(line => line.StartsWith("- ")).Should().Be(12);
        changes3.Count(line => line.StartsWith("  ")).Should().Be(0);

        version3.Should().BeEmpty();
        currentConfiguration.Should().BeEmpty();

        var diff1To3 = await configs.GetConfigurationVersionChangesAsync(key, 1, 3);
        diff1To3.Count(line => line.StartsWith("- ")).Should().Be(5);
        diff1To3.Count(line => line.StartsWith("+ ")).Should().Be(0);

        var diff3To2 = await configs.GetConfigurationVersionChangesAsync(key, 3, 2);
        diff3To2.Count(line => line.StartsWith("+ ")).Should().Be(12);
        diff3To2.Count(line => line.StartsWith("- ")).Should().Be(0);

        var diff2To1 = await configs.GetConfigurationVersionChangesAsync(key, 2, 1);
        diff2To1.Count(line => line.StartsWith("- ")).Should().Be(8);
        diff2To1.Count(line => line.StartsWith("+ ")).Should().Be(1);

        var diff3To0 = await configs.GetConfigurationVersionChangesAsync(key, 3, 0);
        diff3To0.Count(line => line.StartsWith("- ")).Should().Be(0);
        diff3To0.Count(line => line.StartsWith("+ ")).Should().Be(0);
    }

    [Fact]
    public async Task HierarchyView_NonExistingAnnotation_ReturnsNull()
    {
        var configs = Context.Services.GetRequiredService<IConfigurationService>();

        var key = FullKey.Create(
            AnnotationKey.CreateUnitOfExecution("hv-ne-subject", "hv-ne-resp", "hv-ne-ctx", "hv-ne-unit"),
            TestConstants.Organization, Constants.DefaultProjectName, Constants.DefaultViewName);

        var result = await configs.GetConfigurationHierarchyViewAsync(key);

        result.Should().BeNull();
    }

    [Fact]
    public async Task HierarchyView_FullUnitOfExecutionTree_ReturnsAllSevenLevels()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();
        var configs = Context.Services.GetRequiredService<IConfigurationService>();

        const string subjectName = "hv-full-subject";
        const string responsibilityName = "hv-full-resp";
        const string contextName = "hv-full-ctx";
        const string unitName = "hv-full-unit";

        var uoeAnnotationKey = AnnotationKey.CreateUnitOfExecution(subjectName, responsibilityName, contextName, unitName);
        var subjectAnnotationKey = AnnotationKey.CreateSubject(subjectName);
        var responsibilityAnnotationKey = AnnotationKey.CreateResponsibility(responsibilityName);
        var usageAnnotationKey = AnnotationKey.CreateUsage(subjectName, responsibilityName);
        var contextAnnotationKey = AnnotationKey.CreateContext(subjectName, contextName);
        var executionAnnotationKey = AnnotationKey.CreateExecution(subjectName, responsibilityName, contextName);
        var unitAnnotationKey = AnnotationKey.CreateUnit(responsibilityName, unitName);

        await annotations.CreateAnnotationAsync(TestConstants.Organization, new UnitOfExecution
        {
            AnnotationKey = uoeAnnotationKey,
            AnnotationType = AnnotationType.UnitOfExecution,
            Name = unitName,
            SubjectKey = subjectAnnotationKey,
            SubjectName = subjectName,
            ResponsibilityKey = responsibilityAnnotationKey,
            ResponsibilityName = responsibilityName,
            ContextKey = contextAnnotationKey,
            ContextName = contextName,
            UnitKey = unitAnnotationKey,
            UnitName = unitName,
            ProjectName = Constants.DefaultProjectName,
            ViewName = Constants.DefaultViewName,
        });

        var org = TestConstants.Organization;
        var project = Constants.DefaultProjectName;
        var view = Constants.DefaultViewName;

        var uoeKey = FullKey.Create(uoeAnnotationKey, org, project, view);
        var subjectKey = FullKey.Create(subjectAnnotationKey, org, project, view);
        var responsibilityKey = FullKey.Create(responsibilityAnnotationKey, org, project, view);
        var usageKey = FullKey.Create(usageAnnotationKey, org, project, view);
        var contextKey = FullKey.Create(contextAnnotationKey, org, project, view);
        var executionKey = FullKey.Create(executionAnnotationKey, org, project, view);
        var unitKey = FullKey.Create(unitAnnotationKey, org, project, view);

        await configs.CreateOrUpdateConfigurationAsync(responsibilityKey, JObject.Parse("""{ "level": "responsibility" }"""), "system");
        await configs.CreateOrUpdateConfigurationAsync(subjectKey, JObject.Parse("""{ "level": "subject" }"""), "system");
        await configs.CreateOrUpdateConfigurationAsync(usageKey, JObject.Parse("""{ "level": "usage" }"""), "system");
        await configs.CreateOrUpdateConfigurationAsync(contextKey, JObject.Parse("""{ "level": "context" }"""), "system");
        await configs.CreateOrUpdateConfigurationAsync(executionKey, JObject.Parse("""{ "level": "execution" }"""), "system");
        await configs.CreateOrUpdateConfigurationAsync(unitKey, JObject.Parse("""{ "level": "unit" }"""), "system");
        await configs.CreateOrUpdateConfigurationAsync(uoeKey, JObject.Parse("""{ "level": "unit-of-execution" }"""), "system");

        var hierarchy = await configs.GetConfigurationHierarchyViewAsync(uoeKey);

        hierarchy.Should().NotBeNull();
        hierarchy.Should().HaveCount(7);

        hierarchy.Should().ContainKey(responsibilityAnnotationKey.ToString());
        hierarchy.Should().ContainKey(subjectAnnotationKey.ToString());
        hierarchy.Should().ContainKey(usageAnnotationKey.ToString());
        hierarchy.Should().ContainKey(contextAnnotationKey.ToString());
        hierarchy.Should().ContainKey(executionAnnotationKey.ToString());
        hierarchy.Should().ContainKey(unitAnnotationKey.ToString());
        hierarchy.Should().ContainKey(uoeAnnotationKey.ToString());

        // Each entry must contain only its own raw content — no merging from ancestors
        var respEntry = (JObject)hierarchy[responsibilityAnnotationKey.ToString()]!;
        var subjectEntry = (JObject)hierarchy[subjectAnnotationKey.ToString()]!;
        var usageEntry = (JObject)hierarchy[usageAnnotationKey.ToString()]!;
        var contextEntry = (JObject)hierarchy[contextAnnotationKey.ToString()]!;
        var executionEntry = (JObject)hierarchy[executionAnnotationKey.ToString()]!;
        var unitEntry = (JObject)hierarchy[unitAnnotationKey.ToString()]!;
        var uoeEntry = (JObject)hierarchy[uoeAnnotationKey.ToString()]!;

        respEntry.Should().HaveCount(1);
        subjectEntry.Should().HaveCount(1);
        usageEntry.Should().HaveCount(1);
        contextEntry.Should().HaveCount(1);
        executionEntry.Should().HaveCount(1);
        unitEntry.Should().HaveCount(1);
        uoeEntry.Should().HaveCount(1);

        respEntry["level"]!.Value<string>().Should().Be("responsibility");
        subjectEntry["level"]!.Value<string>().Should().Be("subject");
        usageEntry["level"]!.Value<string>().Should().Be("usage");
        contextEntry["level"]!.Value<string>().Should().Be("context");
        executionEntry["level"]!.Value<string>().Should().Be("execution");
        unitEntry["level"]!.Value<string>().Should().Be("unit");
        uoeEntry["level"]!.Value<string>().Should().Be("unit-of-execution");
    }

    [Fact]
    public async Task HierarchyView_SomeLevelsWithoutConfiguration_ExcludesThoseLevels()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();
        var configs = Context.Services.GetRequiredService<IConfigurationService>();

        const string subjectName = "hv-partial-subject";
        const string responsibilityName = "hv-partial-resp";
        const string contextName = "hv-partial-ctx";
        const string unitName = "hv-partial-unit";

        var uoeAnnotationKey = AnnotationKey.CreateUnitOfExecution(subjectName, responsibilityName, contextName, unitName);
        var subjectAnnotationKey = AnnotationKey.CreateSubject(subjectName);
        var responsibilityAnnotationKey = AnnotationKey.CreateResponsibility(responsibilityName);
        var unitAnnotationKey = AnnotationKey.CreateUnit(responsibilityName, unitName);

        await annotations.CreateAnnotationAsync(TestConstants.Organization, new UnitOfExecution
        {
            AnnotationKey = uoeAnnotationKey,
            AnnotationType = AnnotationType.UnitOfExecution,
            Name = unitName,
            SubjectKey = subjectAnnotationKey,
            SubjectName = subjectName,
            ResponsibilityKey = responsibilityAnnotationKey,
            ResponsibilityName = responsibilityName,
            ContextKey = AnnotationKey.CreateContext(subjectName, contextName),
            ContextName = contextName,
            UnitKey = unitAnnotationKey,
            UnitName = unitName,
            ProjectName = Constants.DefaultProjectName,
            ViewName = Constants.DefaultViewName,
        });

        var org = TestConstants.Organization;
        var project = Constants.DefaultProjectName;
        var view = Constants.DefaultViewName;

        var uoeKey = FullKey.Create(uoeAnnotationKey, org, project, view);
        var subjectKey = FullKey.Create(subjectAnnotationKey, org, project, view);
        var responsibilityKey = FullKey.Create(responsibilityAnnotationKey, org, project, view);

        // Only configure 3 of the 7 levels
        await configs.CreateOrUpdateConfigurationAsync(responsibilityKey, JObject.Parse("""{ "from": "responsibility" }"""), "system");
        await configs.CreateOrUpdateConfigurationAsync(subjectKey, JObject.Parse("""{ "from": "subject" }"""), "system");
        await configs.CreateOrUpdateConfigurationAsync(uoeKey, JObject.Parse("""{ "from": "unit-of-execution" }"""), "system");

        var hierarchy = await configs.GetConfigurationHierarchyViewAsync(uoeKey);

        hierarchy.Should().NotBeNull();
        hierarchy.Should().HaveCount(3);
        hierarchy.Should().ContainKey(responsibilityAnnotationKey.ToString());
        hierarchy.Should().ContainKey(subjectAnnotationKey.ToString());
        hierarchy.Should().ContainKey(uoeAnnotationKey.ToString());
        hierarchy.Should().NotContainKey(AnnotationKey.CreateUsage(subjectName, responsibilityName).ToString());
        hierarchy.Should().NotContainKey(AnnotationKey.CreateContext(subjectName, contextName).ToString());
        hierarchy.Should().NotContainKey(AnnotationKey.CreateExecution(subjectName, responsibilityName, contextName).ToString());
        hierarchy.Should().NotContainKey(AnnotationKey.CreateUnit(responsibilityName, unitName).ToString());
    }

    [Fact]
    public async Task GetConfigurationViewChanges()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();
        var configs = Context.Services.GetRequiredService<IConfigurationService>();

        var annotationKey = AnnotationKey.CreateResponsibility("view-diff");
        var viewA = "view-a";
        var viewB = "view-b";

        // Create annotation in both views
        await annotations.CreateAnnotationAsync(TestConstants.Organization, new Responsibility
        {
            AnnotationKey = annotationKey,
            AnnotationType = AnnotationType.Responsibility,
            Name = "view-diff",
            ProjectName = Constants.DefaultProjectName,
            ViewName = viewA,
        });

        await annotations.CreateAnnotationAsync(TestConstants.Organization, new Responsibility
        {
            AnnotationKey = annotationKey,
            AnnotationType = AnnotationType.Responsibility,
            Name = "view-diff",
            ProjectName = Constants.DefaultProjectName,
            ViewName = viewB,
        });

        var configA = JObject.Parse("""
                                    {
                                        "key": "value-a",
                                        "common": "same"
                                    }
                                    """);

        var configB = JObject.Parse("""
                                    {
                                        "key": "value-b",
                                        "common": "same"
                                    }
                                    """);

        var fullKeyA = FullKey.Create(annotationKey, TestConstants.Organization, Constants.DefaultProjectName, viewA);
        var fullKeyB = FullKey.Create(annotationKey, TestConstants.Organization, Constants.DefaultProjectName, viewB);

        await configs.CreateOrUpdateConfigurationAsync(fullKeyA, configA, "system");
        await configs.CreateOrUpdateConfigurationAsync(fullKeyB, configB, "system");

        var diff = await configs.GetConfigurationViewChangesAsync(fullKeyA, viewB);

        diff.Should().NotBeNull();
        // expect - "key": "value-a"
        // expect + "key": "value-b"
        // expect   "common": "same" (and brackets)
        diff.Count(line => line.StartsWith("- ")).Should().Be(1);
        diff.Count(line => line.StartsWith("+ ")).Should().Be(1);
        diff.Count(line => line.StartsWith("  ")).Should().BeGreaterThan(0);
        diff.Should().Contain(line => line.Contains("value-a") && line.StartsWith("- "));
        diff.Should().Contain(line => line.Contains("value-b") && line.StartsWith("+ "));
    }
}
