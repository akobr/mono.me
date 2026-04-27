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

public class CosmosConfigurationSchemaServiceTests(Startup startup)
    : BaseTestsClass(startup)
{
    [Fact]
    public async Task TypeLevelSchema_SetAndGet()
    {
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();

        var schema = JObject.Parse("""
            {
                "type": "object",
                "properties": {
                    "name": { "type": "string" }
                }
            }
            """);

        var result = await schemas.SetSchemaAsync(TestConstants.Organization, Constants.DefaultProjectName, AnnotationTypeCodes.Responsibility, schema, "test");
        result.AnnotationType.Should().Be(AnnotationTypeCodes.Responsibility);
        result.Version.Should().Be(1);

        var retrieved = await schemas.GetSchemaAsync(TestConstants.Organization, Constants.DefaultProjectName, AnnotationTypeCodes.Responsibility);
        retrieved.Should().NotBeNull();
        retrieved!.Content["properties"]!["name"]!["type"]!.Value<string>().Should().Be("string");
    }

    [Fact]
    public async Task AnnotationLevelSchema_SetGetDelete()
    {
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        var annotationKey = "rst.schema-ann-test";

        var schema = JObject.Parse("""
            {
                "type": "object",
                "properties": {
                    "url": { "type": "string", "format": "uri" }
                },
                "required": ["url"]
            }
            """);

        var result = await schemas.SetAnnotationSchemaAsync(TestConstants.Organization, Constants.DefaultProjectName, annotationKey, schema, "test");
        result.AnnotationKey.Should().Be(annotationKey);
        result.Version.Should().Be(1);

        var retrieved = await schemas.GetAnnotationSchemaAsync(TestConstants.Organization, Constants.DefaultProjectName, annotationKey);
        retrieved.Should().NotBeNull();
        retrieved!.AnnotationKey.Should().Be(annotationKey);

        var deleted = await schemas.DeleteAnnotationSchemaAsync(TestConstants.Organization, Constants.DefaultProjectName, annotationKey);
        deleted.Should().BeTrue();

        var afterDelete = await schemas.GetAnnotationSchemaAsync(TestConstants.Organization, Constants.DefaultProjectName, annotationKey);
        afterDelete.Should().BeNull();
    }

    [Fact]
    public async Task DescendantTypeSchema_SetGetDelete()
    {
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        var annotationKey = "rst.schema-dt-test";

        var schema = JObject.Parse("""
            {
                "type": "object",
                "properties": {
                    "timeout": { "type": "integer" }
                }
            }
            """);

        var result = await schemas.SetDescendantTypeSchemaAsync(
            TestConstants.Organization, Constants.DefaultProjectName, annotationKey, AnnotationTypeCodes.Execution, schema, "test");
        result.AnnotationType.Should().Be(AnnotationTypeCodes.Execution);
        result.AnnotationKey.Should().Be(annotationKey);
        result.Version.Should().Be(1);

        var retrieved = await schemas.GetDescendantTypeSchemaAsync(
            TestConstants.Organization, Constants.DefaultProjectName, annotationKey, AnnotationTypeCodes.Execution);
        retrieved.Should().NotBeNull();
        retrieved!.AnnotationType.Should().Be(AnnotationTypeCodes.Execution);
        retrieved!.AnnotationKey.Should().Be(annotationKey);

        var deleted = await schemas.DeleteDescendantTypeSchemaAsync(
            TestConstants.Organization, Constants.DefaultProjectName, annotationKey, AnnotationTypeCodes.Execution);
        deleted.Should().BeTrue();

        var afterDelete = await schemas.GetDescendantTypeSchemaAsync(
            TestConstants.Organization, Constants.DefaultProjectName, annotationKey, AnnotationTypeCodes.Execution);
        afterDelete.Should().BeNull();
    }

    [Fact]
    public async Task CombinedSchema_MergesTypeLevelAndAnnotationLevel()
    {
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        var project = "combined-test";

        var typeSchema = JObject.Parse("""
            {
                "type": "object",
                "properties": {
                    "name": { "type": "string" },
                    "shared": { "type": "string" }
                },
                "required": ["name"]
            }
            """);

        var annotationSchema = JObject.Parse("""
            {
                "type": "object",
                "properties": {
                    "url": { "type": "string", "format": "uri" },
                    "shared": { "type": "integer" }
                },
                "required": ["url"]
            }
            """);

        await schemas.SetSchemaAsync(TestConstants.Organization, project, AnnotationTypeCodes.Responsibility, typeSchema, "test");
        await schemas.SetAnnotationSchemaAsync(TestConstants.Organization, project, "rst.combined-target", annotationSchema, "test");

        var combined = await schemas.GetCombinedSchemaAsync(TestConstants.Organization, project, "rst.combined-target");

        combined.Should().NotBeNull();
        combined!.AppliedSchemas.Should().HaveCount(2);

        var merged = combined.MergedContent;
        var properties = (JObject)merged["properties"]!;
        properties.Should().ContainKey("name");
        properties.Should().ContainKey("url");
        properties.Should().ContainKey("shared");

        // More specific (annotation-level) overrides shared property
        properties["shared"]!["type"]!.Value<string>().Should().Be("integer");

        // Required arrays are unioned
        var required = (JArray)merged["required"]!;
        required.Select(t => t.Value<string>()).Should().Contain("name");
        required.Select(t => t.Value<string>()).Should().Contain("url");
    }

    [Fact]
    public async Task CombinedSchema_MergesDescendantTypeSchema()
    {
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        var project = "combined-dt-test";

        var typeSchema = JObject.Parse("""
            {
                "type": "object",
                "properties": {
                    "base": { "type": "string" }
                }
            }
            """);

        var dtSchema = JObject.Parse("""
            {
                "type": "object",
                "properties": {
                    "fromParent": { "type": "boolean" }
                }
            }
            """);

        await schemas.SetSchemaAsync(TestConstants.Organization, project, AnnotationTypeCodes.Execution, typeSchema, "test");
        await schemas.SetDescendantTypeSchemaAsync(TestConstants.Organization, project, "rst.dt-parent", AnnotationTypeCodes.Execution, dtSchema, "test");

        var combined = await schemas.GetCombinedSchemaAsync(TestConstants.Organization, project, "exe.dt-subject.dt-parent.dt-ctx");

        combined.Should().NotBeNull();
        combined!.AppliedSchemas.Should().HaveCount(2);

        var properties = (JObject)combined.MergedContent["properties"]!;
        properties.Should().ContainKey("base");
        properties.Should().ContainKey("fromParent");
    }

    [Fact]
    public async Task CombinedSchema_ReturnsNull_WhenNoSchemasExist()
    {
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();

        var combined = await schemas.GetCombinedSchemaAsync(TestConstants.Organization, "nonexistent-project", "rst.nonexistent");

        combined.Should().BeNull();
    }

    [Fact]
    public async Task ValidateContent_Passes_WhenContentMatchesSchema()
    {
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        var project = "validate-pass-test";

        var schema = JObject.Parse("""
            {
                "type": "object",
                "properties": {
                    "name": { "type": "string" }
                },
                "required": ["name"]
            }
            """);

        await schemas.SetSchemaAsync(TestConstants.Organization, project, AnnotationTypeCodes.Responsibility, schema, "test");

        var content = JObject.Parse("""{ "name": "valid" }""");

        var act = () => schemas.ValidateContentAsync(TestConstants.Organization, project, "rst.validate-pass", content);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ValidateContent_Throws_WhenContentViolatesSchema()
    {
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        var project = "validate-fail-test";

        var schema = JObject.Parse("""
            {
                "type": "object",
                "properties": {
                    "name": { "type": "string" }
                },
                "required": ["name"]
            }
            """);

        await schemas.SetSchemaAsync(TestConstants.Organization, project, AnnotationTypeCodes.Responsibility, schema, "test");

        var content = JObject.Parse("""{ "other": 42 }""");

        var act = () => schemas.ValidateContentAsync(TestConstants.Organization, project, "rst.validate-fail", content);
        await act.Should().ThrowAsync<SchemaValidationException>();
    }

    [Fact]
    public async Task ValidateContent_NoOp_WhenNoSchemaExists()
    {
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();

        var content = JObject.Parse("""{ "anything": "goes" }""");

        var act = () => schemas.ValidateContentAsync(TestConstants.Organization, "no-schema-project", "rst.no-schema", content);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SaveConfiguration_BlockedBySchema()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();
        var configs = Context.Services.GetRequiredService<IConfigurationService>();
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        var project = "save-blocked-test";

        var schema = JObject.Parse("""
            {
                "type": "object",
                "properties": {
                    "name": { "type": "string" }
                },
                "required": ["name"],
                "additionalProperties": false
            }
            """);

        await schemas.SetSchemaAsync(TestConstants.Organization, project, AnnotationTypeCodes.Responsibility, schema, "test");

        var annotationKey = AnnotationKey.CreateResponsibility("save-blocked");
        await annotations.CreateAnnotationAsync(TestConstants.Organization, new Responsibility
        {
            AnnotationKey = annotationKey,
            AnnotationType = AnnotationType.Responsibility,
            Name = "save-blocked",
            ProjectName = project,
            ViewName = Constants.DefaultViewName,
        });

        var invalidContent = JObject.Parse("""{ "badField": 123 }""");
        var key = FullKey.Create(annotationKey, TestConstants.Organization, project, Constants.DefaultViewName);

        var act = () => configs.CreateOrUpdateConfigurationAsync(key, invalidContent, "test");
        await act.Should().ThrowAsync<SchemaValidationException>();
    }

    [Fact]
    public async Task SaveConfiguration_AllowedBySchema()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();
        var configs = Context.Services.GetRequiredService<IConfigurationService>();
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        var project = "save-allowed-test";

        var schema = JObject.Parse("""
            {
                "type": "object",
                "properties": {
                    "name": { "type": "string" }
                },
                "required": ["name"]
            }
            """);

        await schemas.SetSchemaAsync(TestConstants.Organization, project, AnnotationTypeCodes.Responsibility, schema, "test");

        var annotationKey = AnnotationKey.CreateResponsibility("save-allowed");
        await annotations.CreateAnnotationAsync(TestConstants.Organization, new Responsibility
        {
            AnnotationKey = annotationKey,
            AnnotationType = AnnotationType.Responsibility,
            Name = "save-allowed",
            ProjectName = project,
            ViewName = Constants.DefaultViewName,
        });

        var validContent = JObject.Parse("""{ "name": "valid-value" }""");
        var key = FullKey.Create(annotationKey, TestConstants.Organization, project, Constants.DefaultViewName);

        var act = () => configs.CreateOrUpdateConfigurationAsync(key, validContent, "test");
        await act.Should().NotThrowAsync();
    }
}
