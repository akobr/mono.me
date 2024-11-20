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
        var configuration = await configs.GetConfigurationAsync(key);

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
        var configuration = await configs.GetConfigurationAsync(key);

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
        var retrieveConfig = await configs.GetConfigurationAsync(key);

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
        var retrieveConfig = await configs.GetConfigurationAsync(key);

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
        var retrieveConfig = await configs.GetConfigurationAsync(key);

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
        var executionConfigBeforeDelete = await configs.GetConfigurationAsync(executionKey);

        // TODO: [P3] replace it with just calling config service
        await annotations.DeleteAnnotationAsync(subjectKey); // this will delete configuration with descendants
        var executionConfigAfterDelete = await configs.GetConfigurationAsync(executionKey);

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
        var currentConfiguration = await configs.GetConfigurationAsync(key);

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

        changes3.Count(line => line.StartsWith("+ ")).Should().Be(1);
        changes3.Count(line => line.StartsWith("- ")).Should().Be(12);
        changes3.Count(line => line.StartsWith("  ")).Should().Be(0);

        version3.Should().BeEmpty();
        currentConfiguration.Should().BeEmpty();
    }
}
