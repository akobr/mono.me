#nullable enable

using System;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Listings of configurations, schemas and templates; every test works in its own project.
public class CosmosListingTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Organization = TestConstants.Organization;
    private const string View = Constants.DefaultViewName;
    private const string OtherView = "rollout";

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    private IConfigurationService Configs => Context.Services.GetRequiredService<IConfigurationService>();

    private IConfigurationSchemaService Schemas => Context.Services.GetRequiredService<IConfigurationSchemaService>();

    private IConfigurationTemplateService Templates => Context.Services.GetRequiredService<IConfigurationTemplateService>();

    [Fact]
    public async Task ListConfigurations_ReturnsSummariesWithoutDocuments()
    {
        var project = NewProject();
        var alpha = await CreateConfigurationAsync(project, View, AnnotationKey.CreateResponsibility("alpha"), """{ "retries": 3 }""");
        await CreateConfigurationAsync(project, View, AnnotationKey.CreateSubject("beta"), """{ "tier": "gold" }""");
        var emptied = await CreateConfigurationAsync(project, View, AnnotationKey.CreateResponsibility("emptied"), """{ "a": 1 }""");
        await Configs.PatchConfigurationAsync(emptied, JArray.Parse("""[{ "op": "remove", "path": "/a" }]"""), "author-2");
        await CreateConfigurationAsync(project, OtherView, AnnotationKey.CreateResponsibility("alpha"), """{ "retries": 9 }""");
        var calculated = await Configs.GetRawConfigurationAsync(alpha);

        var response = await Configs.ListConfigurationsAsync(Organization, project, View);

        response.Configurations.Select(item => item.AnnotationKey).Should().BeEquivalentTo(["rst.alpha", "sbt.beta", "rst.emptied"]);
        response.Count.Should().Be(3);
        response.ContinuationToken.Should().BeNull();

        var alphaSummary = response.Configurations.Single(item => item.AnnotationKey == "rst.alpha");
        alphaSummary.AnnotationType.Should().Be(AnnotationType.Responsibility);
        alphaSummary.Version.Should().Be(1);
        alphaSummary.Author.Should().Be("author-1");
        alphaSummary.HasContent.Should().BeTrue();
        alphaSummary.Hash.Should().Be(calculated!.Hash);
        alphaSummary.UpdatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));

        var emptiedSummary = response.Configurations.Single(item => item.AnnotationKey == "rst.emptied");
        emptiedSummary.HasContent.Should().BeFalse();
        emptiedSummary.Version.Should().Be(2);
        response.Configurations.Single(item => item.AnnotationKey == "sbt.beta").AnnotationType.Should().Be(AnnotationType.Subject);
    }

    [Fact]
    public async Task ListConfigurations_FiltersByTypeAndKeyPrefixAndView()
    {
        var project = NewProject();
        await CreateConfigurationAsync(project, View, AnnotationKey.CreateResponsibility("alpha"), """{ "a": 1 }""");
        await CreateConfigurationAsync(project, View, AnnotationKey.CreateResponsibility("alto"), """{ "a": 1 }""");
        await CreateConfigurationAsync(project, View, AnnotationKey.CreateSubject("alpha"), """{ "a": 1 }""");
        await CreateConfigurationAsync(project, OtherView, AnnotationKey.CreateSubject("gamma"), """{ "a": 1 }""");

        var subjects = await Configs.ListConfigurationsAsync(Organization, project, View, annotationType: "sbt");
        var prefixed = await Configs.ListConfigurationsAsync(Organization, project, View, keyPrefix: "rst.alp");
        var other = await Configs.ListConfigurationsAsync(Organization, project, OtherView);
        var missingProject = await Configs.ListConfigurationsAsync(Organization, NewProject(), View);

        subjects.Configurations.Select(item => item.AnnotationKey).Should().Equal("sbt.alpha");
        prefixed.Configurations.Select(item => item.AnnotationKey).Should().Equal("rst.alpha");
        other.Configurations.Select(item => item.AnnotationKey).Should().Equal("sbt.gamma");
        missingProject.Configurations.Should().BeEmpty();
    }

    [Fact]
    public async Task ListSchemas_ReturnsEveryKindOnceWithTheCurrentVersion()
    {
        var project = NewProject();
        var schema = JObject.Parse("""{ "type": "object" }""");
        var stricter = JObject.Parse("""{ "type": "object", "properties": { "retries": { "type": "integer" } } }""");
        await Schemas.SetSchemaAsync(Organization, project, View, AnnotationTypeCodes.Responsibility, schema, "author-1", force: false);
        await Schemas.SetSchemaAsync(Organization, project, View, AnnotationTypeCodes.Responsibility, stricter, "author-2", force: false);
        await Schemas.SetAnnotationSchemaAsync(Organization, project, View, "rst.alpha", schema, "author-1", force: false);
        await Schemas.SetDescendantTypeSchemaAsync(Organization, project, View, "rst.alpha", AnnotationTypeCodes.Unit, schema, "author-1", force: false);
        await Schemas.SetSchemaAsync(Organization, project, OtherView, AnnotationTypeCodes.Subject, schema, "author-1", force: false);

        var summaries = await Schemas.ListSchemasAsync(Organization, project, View);

        summaries.Select(item => (item.Kind, item.AnnotationType, item.AnnotationKey, item.Version)).Should().Equal(
            (ConfigurationSchemaKind.Type, AnnotationTypeCodes.Responsibility, (string?)null, 2UL),
            (ConfigurationSchemaKind.Annotation, (string?)null, "rst.alpha", 1UL),
            (ConfigurationSchemaKind.DescendantType, AnnotationTypeCodes.Unit, "rst.alpha", 1UL));
        summaries[0].Author.Should().Be("author-2");
        summaries.Should().OnlyContain(item => item.UpdatedAt != null);
    }

    [Fact]
    public async Task ListTemplates_ReturnsTheCurrentTemplatesOfTheView()
    {
        var project = NewProject();
        await Templates.CreateOrUpdateTemplateAsync(Organization, project, View, AnnotationTypeCodes.Execution, JObject.Parse("""{ "a": 1 }"""), "author-1");
        await Templates.CreateOrUpdateTemplateAsync(Organization, project, View, AnnotationTypeCodes.Execution, JObject.Parse("""{ "a": 2 }"""), "author-2");
        await Templates.CreateOrUpdateTemplateAsync(Organization, project, View, AnnotationTypeCodes.Responsibility, JObject.Parse("""{ "b": 1 }"""), "author-1");
        await Templates.CreateOrUpdateTemplateAsync(Organization, project, OtherView, AnnotationTypeCodes.Subject, JObject.Parse("""{ "c": 1 }"""), "author-1");

        var summaries = await Templates.ListTemplatesAsync(Organization, project, View);

        summaries.Select(item => (item.AnnotationType, item.Version, item.Author)).Should().Equal(
            (AnnotationTypeCodes.Execution, 2UL, "author-2"),
            (AnnotationTypeCodes.Responsibility, 1UL, "author-1"));
    }

    private static string NewProject() => $"lst-{Guid.NewGuid():N}"[..16];

    private async Task<FullKey> CreateConfigurationAsync(string project, string view, AnnotationKey annotationKey, string content)
    {
        var name = annotationKey.ToString().Split('.')[^1];
        Annotation annotation = annotationKey.Type == AnnotationType.Subject
            ? new Subject { AnnotationKey = annotationKey, AnnotationType = AnnotationType.Subject, Name = name, ProjectName = project, ViewName = view }
            : new Responsibility { AnnotationKey = annotationKey, AnnotationType = AnnotationType.Responsibility, Name = name, ProjectName = project, ViewName = view };
        await Annotations.CreateAnnotationAsync(Organization, annotation);

        var key = FullKey.Create(annotationKey, Organization, project, view);
        await Configs.CreateOrUpdateConfigurationAsync(key, JObject.Parse(content), "author-1");
        return key;
    }
}
