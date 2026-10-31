using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.Entities;
using _42.Platform.Storyteller.Entities.Configurations;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

public class CosmosConfigurationSchemaServiceTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string Project = "schema-tests";
    private const string View = Constants.DefaultViewName;
    private const string OtherView = "rollout";

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

        var result = await schemas.SetSchemaAsync(TestConstants.Organization, Project, View, AnnotationTypeCodes.Responsibility, schema, AuditActor.Account("test"), false);
        result.View.Should().Be(View);
        result.AnnotationType.Should().Be(AnnotationTypeCodes.Responsibility);
        result.Version.Should().Be(1);

        var retrieved = await schemas.GetSchemaAsync(TestConstants.Organization, Project, View, AnnotationTypeCodes.Responsibility);
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

        var result = await schemas.SetAnnotationSchemaAsync(TestConstants.Organization, Project, View, annotationKey, schema, AuditActor.Account("test"), false);
        result.View.Should().Be(View);
        result.AnnotationKey.Should().Be(annotationKey);
        result.Version.Should().Be(1);

        var retrieved = await schemas.GetAnnotationSchemaAsync(TestConstants.Organization, Project, View, annotationKey);
        retrieved.Should().NotBeNull();
        retrieved!.AnnotationKey.Should().Be(annotationKey);

        var deleted = await schemas.DeleteAnnotationSchemaAsync(TestConstants.Organization, Project, View, annotationKey);
        deleted.Should().BeTrue();

        var afterDelete = await schemas.GetAnnotationSchemaAsync(TestConstants.Organization, Project, View, annotationKey);
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
            TestConstants.Organization, Project, View, annotationKey, AnnotationTypeCodes.Execution, schema, AuditActor.Account("test"), false);
        result.View.Should().Be(View);
        result.AnnotationType.Should().Be(AnnotationTypeCodes.Execution);
        result.AnnotationKey.Should().Be(annotationKey);
        result.Version.Should().Be(1);

        var retrieved = await schemas.GetDescendantTypeSchemaAsync(
            TestConstants.Organization, Project, View, annotationKey, AnnotationTypeCodes.Execution);
        retrieved.Should().NotBeNull();
        retrieved!.AnnotationType.Should().Be(AnnotationTypeCodes.Execution);
        retrieved!.AnnotationKey.Should().Be(annotationKey);

        var deleted = await schemas.DeleteDescendantTypeSchemaAsync(
            TestConstants.Organization, Project, View, annotationKey, AnnotationTypeCodes.Execution);
        deleted.Should().BeTrue();

        var afterDelete = await schemas.GetDescendantTypeSchemaAsync(
            TestConstants.Organization, Project, View, annotationKey, AnnotationTypeCodes.Execution);
        afterDelete.Should().BeNull();
    }

    [Fact]
    public async Task DescendantTypeSchema_OfADeepPair_SetGetDelete()
    {
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        var project = "dt-deep-pair";
        var annotationKey = "exe.dt-subject.dt-parent.dt-ctx";

        var result = await schemas.SetDescendantTypeSchemaAsync(
            TestConstants.Organization, project, View, annotationKey, "UXE", StrictNameSchema(), AuditActor.Account("test"), false);
        result.AnnotationType.Should().Be(AnnotationTypeCodes.UnitOfExecution);
        result.AnnotationKey.Should().Be(annotationKey);
        result.Version.Should().Be(1);

        (await schemas.GetDescendantTypeSchemaAsync(TestConstants.Organization, project, View, annotationKey, AnnotationTypeCodes.UnitOfExecution))
            .Should().NotBeNull();
        (await schemas.DeleteDescendantTypeSchemaAsync(TestConstants.Organization, project, View, annotationKey, AnnotationTypeCodes.UnitOfExecution))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DescendantTypeSchema_OfATypeThatCannotDescend_IsRejectedAndNothingIsStored(bool force)
    {
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        var project = force ? "dt-cannot-descend-forced" : "dt-cannot-descend";
        var annotationKey = "exe.s.r.c";

        var act = () => schemas.SetDescendantTypeSchemaAsync(
            TestConstants.Organization, project, View, annotationKey, AnnotationTypeCodes.Responsibility, StrictNameSchema(), AuditActor.Account("test"), force);

        var exception = await act.Should().ThrowAsync<InvalidInputException>();
        exception.Which.ErrorCode.Should().Be(ErrorCodes.InvalidKey);
        exception.Which.Message.Should().Be("The type rst cannot descend from exe.s.r.c, so it has no descendant-type schema there.");

        (await schemas.GetDescendantTypeSchemaAsync(TestConstants.Organization, project, View, annotationKey, AnnotationTypeCodes.Responsibility))
            .Should().BeNull();
        (await schemas.GetDescendantTypeSchemaVersionsAsync(TestConstants.Organization, project, View, annotationKey, AnnotationTypeCodes.Responsibility))
            .Should().BeEmpty();
        (await schemas.ListSchemasAsync(TestConstants.Organization, project, View)).Should().BeEmpty();
    }

    [Fact]
    public async Task DescendantTypeSchema_StoredBeforeTheRule_StaysReadableAndDeletable()
    {
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        var containers = Context.Services.GetRequiredService<IContainerRepositoryProvider>();
        var project = "dt-legacy-cannot-descend";
        var annotationKey = "exe.s.r.c";
        var typeCode = AnnotationTypeCodes.Responsibility;

        // a document an earlier version of the service stored: the ids of SchemaIdentity.ForDescendant
        await containers.GetOrganizationContainer(TestConstants.Organization).Container.CreateItemAsync(
            new ConfigurationSchemaEntity
            {
                PartitionKey = $"{project}.schema",
                Id = $"{View}.{EntityIdPrefixTypes.ConfigurationSchema}.d.{typeCode}.{annotationKey}",
                AnnotationKey = annotationKey,
                Name = $"dt.{typeCode}.{annotationKey}",
                ProjectName = project,
                ViewName = View,
                Content = StrictNameSchema(),
                Modified = AuditStamp.Of(AuditActor.System, DateTimeOffset.UtcNow),
                Version = 1,
            },
            new PartitionKey($"{project}.schema"));

        var stored = await schemas.GetDescendantTypeSchemaAsync(TestConstants.Organization, project, View, annotationKey, typeCode);
        stored.Should().NotBeNull();
        stored!.AnnotationType.Should().Be(typeCode);
        stored.AnnotationKey.Should().Be(annotationKey);
        stored.Version.Should().Be(1);

        (await schemas.GetDescendantTypeSchemaVersionsAsync(TestConstants.Organization, project, View, annotationKey, typeCode))
            .Should().ContainSingle();
        (await schemas.ListSchemasAsync(TestConstants.Organization, project, View))
            .Should().ContainSingle(summary => summary.Kind == ConfigurationSchemaKind.DescendantType && summary.AnnotationKey == annotationKey);

        (await schemas.DeleteDescendantTypeSchemaAsync(TestConstants.Organization, project, View, annotationKey, typeCode)).Should().BeTrue();
        (await schemas.GetDescendantTypeSchemaAsync(TestConstants.Organization, project, View, annotationKey, typeCode)).Should().BeNull();
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

        await schemas.SetSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility, typeSchema, AuditActor.Account("test"), false);
        await schemas.SetAnnotationSchemaAsync(TestConstants.Organization, project, View, "rst.combined-target", annotationSchema, AuditActor.Account("test"), false);

        var combined = await schemas.GetCombinedSchemaAsync(TestConstants.Organization, project, View, "rst.combined-target");

        combined.Should().NotBeNull();
        combined!.View.Should().Be(View);
        combined.AppliedSchemas.Should().HaveCount(2);
        combined.AppliedSchemas[0].AnnotationType.Should().Be(AnnotationTypeCodes.Responsibility);
        combined.AppliedSchemas[1].AnnotationKey.Should().Be("rst.combined-target");

        var merged = combined.MergedContent;
        var properties = (JObject)merged["properties"]!;
        properties.Should().ContainKey("name");
        properties.Should().ContainKey("url");
        properties.Should().ContainKey("shared");

        properties["shared"]!["type"]!.Value<string>().Should().Be("integer");

        var required = (JArray)merged["required"]!;
        required.Select(token => token.Value<string>()).Should().Contain("name");
        required.Select(token => token.Value<string>()).Should().Contain("url");
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

        await schemas.SetSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Execution, typeSchema, AuditActor.Account("test"), false);
        await schemas.SetDescendantTypeSchemaAsync(TestConstants.Organization, project, View, "rst.dt-parent", AnnotationTypeCodes.Execution, dtSchema, AuditActor.Account("test"), false);

        var combined = await schemas.GetCombinedSchemaAsync(TestConstants.Organization, project, View, "exe.dt-subject.dt-parent.dt-ctx");

        combined.Should().NotBeNull();
        combined!.View.Should().Be(View);
        combined.AppliedSchemas.Should().HaveCount(2);
        combined.AppliedSchemas[0].AnnotationType.Should().Be(AnnotationTypeCodes.Execution);
        combined.AppliedSchemas[0].AnnotationKey.Should().BeNull();
        combined.AppliedSchemas[1].AnnotationType.Should().Be(AnnotationTypeCodes.Execution);
        combined.AppliedSchemas[1].AnnotationKey.Should().Be("rst.dt-parent");

        var properties = (JObject)combined.MergedContent["properties"]!;
        properties.Should().ContainKey("base");
        properties.Should().ContainKey("fromParent");
    }

    [Fact]
    public async Task CombinedSchema_ReturnsNull_WhenNoSchemasExist()
    {
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();

        var combined = await schemas.GetCombinedSchemaAsync(TestConstants.Organization, "nonexistent-project", View, "rst.nonexistent");

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

        await schemas.SetSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility, schema, AuditActor.Account("test"), false);

        var content = JObject.Parse("""{ "name": "valid" }""");

        var act = () => schemas.ValidateContentAsync(TestConstants.Organization, project, View, "rst.validate-pass", content);
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

        await schemas.SetSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility, schema, AuditActor.Account("test"), false);

        var content = JObject.Parse("""{ "other": 42 }""");

        var act = () => schemas.ValidateContentAsync(TestConstants.Organization, project, View, "rst.validate-fail", content);
        var exception = await act.Should().ThrowAsync<SchemaValidationException>();
        exception.Which.ValidationErrors.Should().ContainSingle();
        exception.Which.ValidationErrors[0].ViewName.Should().Be(View);
        exception.Which.ValidationErrors[0].AnnotationKey.Should().Be("rst.validate-fail");
    }

    [Fact]
    public async Task ValidateContent_NoOp_WhenNoSchemaExists()
    {
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();

        var content = JObject.Parse("""{ "anything": "goes" }""");

        var act = () => schemas.ValidateContentAsync(TestConstants.Organization, "no-schema-project", View, "rst.no-schema", content);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SaveConfiguration_BlockedBySchema()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();
        var configs = Context.Services.GetRequiredService<IConfigurationService>();
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        var project = "save-blocked-test";

        await schemas.SetSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility, StrictNameSchema(), AuditActor.Account("test"), false);
        var key = await CreateResponsibilityAsync(annotations, project, "save-blocked", View);

        var act = () => configs.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{ "badField": 123 }"""), AuditActor.Account("test"));
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

        await schemas.SetSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility, schema, AuditActor.Account("test"), false);
        var key = await CreateResponsibilityAsync(annotations, project, "save-allowed", View);

        var act = () => configs.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{ "name": "valid-value" }"""), AuditActor.Account("test"));
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SchemaInOneView_DoesNotAffectAnotherView()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();
        var configs = Context.Services.GetRequiredService<IConfigurationService>();
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        var project = "view-isolation-test";

        await schemas.SetSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility, StrictNameSchema(), AuditActor.Account("test"), false);
        var defaultKey = await CreateResponsibilityAsync(annotations, project, "isolated", View);
        var otherKey = await CreateResponsibilityAsync(annotations, project, "isolated", OtherView);
        var invalid = JObject.Parse("""{ "badField": 1 }""");

        var otherView = () => configs.CreateOrUpdateConfigurationAsync(otherKey, invalid, AuditActor.Account("test"));
        await otherView.Should().NotThrowAsync();

        var sameView = () => configs.CreateOrUpdateConfigurationAsync(defaultKey, invalid, AuditActor.Account("test"));
        await sameView.Should().ThrowAsync<SchemaValidationException>();
    }

    [Fact]
    public async Task SchemaSave_RejectsFailingConfigurationInTheView_AndIgnoresAnotherView()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();
        var configs = Context.Services.GetRequiredService<IConfigurationService>();
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        var project = "compliance-view-test";
        var invalid = JObject.Parse("""{ "badField": 1 }""");

        var defaultKey = await CreateResponsibilityAsync(annotations, project, "comply-default", View);
        var otherKey = await CreateResponsibilityAsync(annotations, project, "comply-other", OtherView);
        await configs.CreateOrUpdateConfigurationAsync(defaultKey, invalid, AuditActor.Account("test"));
        await configs.CreateOrUpdateConfigurationAsync(otherKey, invalid, AuditActor.Account("test"));

        var act = () => schemas.SetSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility, StrictNameSchema(), AuditActor.Account("test"), false);
        var exception = await act.Should().ThrowAsync<SchemaValidationException>();
        exception.Which.ValidationErrors.Should().ContainSingle();
        exception.Which.ValidationErrors[0].ViewName.Should().Be(View);
        exception.Which.ValidationErrors[0].AnnotationKey.Should().Be(defaultKey.Annotation.ToString());

        (await schemas.GetSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility)).Should().BeNull();
    }

    [Fact]
    public async Task SchemaSave_ForcePersists_AndRejectsUnparsableBody()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();
        var configs = Context.Services.GetRequiredService<IConfigurationService>();
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        var project = "force-schema-test";

        var key = await CreateResponsibilityAsync(annotations, project, "forced", View);
        await configs.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{ "badField": 1 }"""), AuditActor.Account("test"));

        var stored = await schemas.SetSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility, StrictNameSchema(), AuditActor.Account("test"), true);
        stored.Version.Should().Be(1);

        var invalid = JObject.Parse("""{ "type": "object", "properties": [] }""");
        var act = () => schemas.SetSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility, invalid, AuditActor.Account("test"), true);
        (await act.Should().ThrowAsync<InvalidInputException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvalidSchema);
        (await schemas.GetSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility))!.Version.Should().Be(1);
    }

    [Fact]
    public async Task SchemaSave_UnchangedContent_DoesNotIncrementVersion()
    {
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        var project = "equal-schema-test";
        var schema = JObject.Parse("""{ "type": "object", "properties": { "name": { "type": "string" } } }""");

        var first = await schemas.SetSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility, schema, AuditActor.Account("ada"), false);
        var second = await schemas.SetSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility, schema, AuditActor.Account("grace"), false);

        first.Version.Should().Be(1);
        second.Version.Should().Be(1);
        second.Modified.By.Id.Should().Be("ada");
    }

    [Fact]
    public async Task SchemaSave_DeleteThenCreate_ContinuesAfterLastVersion()
    {
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        var project = "delete-continue-test";
        var firstBody = JObject.Parse("""{ "type": "object", "properties": { "name": { "type": "string" } } }""");
        var secondBody = JObject.Parse("""{ "type": "object", "properties": { "count": { "type": "integer" } } }""");

        (await schemas.SetSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility, firstBody, AuditActor.Account("test"), false)).Version.Should().Be(1);
        (await schemas.SetSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility, secondBody, AuditActor.Account("test"), false)).Version.Should().Be(2);
        (await schemas.DeleteSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility)).Should().BeTrue();
        (await schemas.GetSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility)).Should().BeNull();

        var recreated = await schemas.SetSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility, firstBody, AuditActor.Account("test"), false);
        recreated.Version.Should().Be(3);
    }

    [Fact]
    public async Task SchemaVersions_ListContentAndDiff()
    {
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        var project = "schema-versions-test";
        var firstBody = JObject.Parse("""{ "type": "object", "properties": { "name": { "type": "string" } } }""");
        var secondBody = JObject.Parse("""{ "type": "object", "properties": { "name": { "type": "string" }, "count": { "type": "integer" } } }""");

        await schemas.SetSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility, firstBody, AuditActor.Account("test"), false);
        await schemas.SetSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility, secondBody, AuditActor.Account("test"), false);

        var versions = await schemas.GetSchemaVersionsAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility);
        versions.Select(version => version.Version).Should().Equal(1u, 2u);
        versions.Single(version => version.Version == 2).ExpirationTime.Should().Be(DateTimeOffset.MaxValue);

        var stored = await schemas.GetSchemaVersionContentAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility, 1);
        stored.Should().NotBeNull();
        stored!.Content["properties"]!["name"].Should().NotBeNull();
        stored.Content["properties"]!["count"].Should().BeNull();

        var diff = await schemas.GetSchemaVersionChangesAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility, 2);
        diff.Stats.Additions.Should().BeGreaterThan(0);

        var emptyTypeDiff = await schemas.GetSchemaVersionChangesAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility, 0);
        emptyTypeDiff.Stats.Additions.Should().Be(0);
        emptyTypeDiff.Stats.Deletions.Should().Be(0);

        var emptyAnnotationDiff = await schemas.GetAnnotationSchemaVersionChangesAsync(TestConstants.Organization, project, View, "rst.schema-versions", 0);
        emptyAnnotationDiff.Stats.Additions.Should().Be(0);
        emptyAnnotationDiff.Stats.Deletions.Should().Be(0);

        var emptyDescendantDiff = await schemas.GetDescendantTypeSchemaVersionChangesAsync(
            TestConstants.Organization,
            project,
            View,
            "rst.schema-versions",
            AnnotationTypeCodes.Unit,
            0);
        emptyDescendantDiff.Stats.Additions.Should().Be(0);
        emptyDescendantDiff.Stats.Deletions.Should().Be(0);
    }

    [Fact]
    public async Task LegacyProjectWideSchema_IsIgnored()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();
        var configs = Context.Services.GetRequiredService<IConfigurationService>();
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        var containers = Context.Services.GetRequiredService<IContainerRepositoryProvider>();
        var project = "legacy-ignored-test";
        var typeCode = AnnotationTypeCodes.Responsibility;

        await containers.GetOrganizationContainer(TestConstants.Organization).Container.UpsertItemAsync(
            new ConfigurationSchemaEntity
            {
                PartitionKey = $"{project}.schema",
                Id = $"{EntityIdPrefixTypes.ConfigurationSchema}.{typeCode}",
                AnnotationKey = typeCode,
                Name = typeCode,
                ProjectName = project,
                ViewName = string.Empty,
                Content = StrictNameSchema(),
                Modified = AuditStamp.Of(AuditActor.System, DateTimeOffset.UtcNow),
                Version = 9,
            },
            new PartitionKey($"{project}.schema"));

        (await schemas.GetSchemaAsync(TestConstants.Organization, project, View, typeCode)).Should().BeNull();
        (await schemas.GetCombinedSchemaAsync(TestConstants.Organization, project, View, "rst.legacy-target")).Should().BeNull();

        var content = JObject.Parse("""{ "badField": 1 }""");
        var validate = () => schemas.ValidateContentAsync(TestConstants.Organization, project, View, "rst.legacy-target", content);
        await validate.Should().NotThrowAsync();

        var key = await CreateResponsibilityAsync(annotations, project, "legacy-target", View);
        var create = () => configs.CreateOrUpdateConfigurationAsync(key, content, AuditActor.Account("test"));
        await create.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ConfigurationCreateAndPatch_RequireForceWhenTheyViolateTheSchema()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();
        var configs = Context.Services.GetRequiredService<IConfigurationService>();
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        var project = "config-force-test";

        await schemas.SetSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility, StrictNameSchema(), AuditActor.Account("test"), false);
        var key = await CreateResponsibilityAsync(annotations, project, "patched", View);

        var create = () => configs.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{ "badField": 1 }"""), AuditActor.Account("test"));
        await create.Should().ThrowAsync<SchemaValidationException>();

        await configs.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{ "name": "ok" }"""), AuditActor.Account("test"));

        var rejectedUpdate = () => configs.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{ "badField": 1 }"""), AuditActor.Account("test"));
        await rejectedUpdate.Should().ThrowAsync<SchemaValidationException>();
        var versionsAfterRejection = await configs.GetConfigurationVersionsAsync(key);
        versionsAfterRejection.Should().ContainSingle();
        versionsAfterRejection.Single().Version.Should().Be(1u);

        var patch = JArray.Parse("""[{ "op": "add", "path": "/badField", "value": 1 }]""");
        var rejected = () => configs.PatchConfigurationAsync(key, patch, AuditActor.Account("test"));
        await rejected.Should().ThrowAsync<SchemaValidationException>();

        var forced = await configs.PatchConfigurationAsync(key, patch, AuditActor.Account("test"), true);
        forced.Content["badField"]!.Value<int>().Should().Be(1);

        var forcedCreate = await configs.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{ "badField": 2 }"""), AuditActor.Account("test"), true);
        forcedCreate.Content["badField"]!.Value<int>().Should().Be(2);
    }

    // Whatever force is, before anything is read or written: the URL is never fetched.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SchemaSave_WithAnExternalReference_IsRefusedWithoutFetchingOrStoring(bool force)
    {
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        var project = force ? "external-ref-forced" : "external-ref";
        var actor = AuditActor.Account("test");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var external = ExternalNameSchema(listener);
        await schemas.SetSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility, StrictNameSchema(), actor, false);

        Func<Task>[] writes =
        [
            () => schemas.SetSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility, external, actor, force),
            () => schemas.SetAnnotationSchemaAsync(TestConstants.Organization, project, View, "rst.external", external, actor, force),
            () => schemas.SetDescendantTypeSchemaAsync(TestConstants.Organization, project, View, "rst.external", AnnotationTypeCodes.Execution, external, actor, force),
        ];

        foreach (var write in writes)
        {
            var refused = (await write.Should().ThrowAsync<InvalidInputException>()).Which;
            refused.ErrorCode.Should().Be(ErrorCodes.ExternalSchemaReference);
            refused.Message.Should().StartWith($"External $ref not allowed: 'http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/name.json'.");
        }

        listener.Pending().Should().BeFalse();
        (await schemas.GetSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility))!.Version.Should().Be(1);
        (await schemas.GetAnnotationSchemaAsync(TestConstants.Organization, project, View, "rst.external")).Should().BeNull();
        (await schemas.GetDescendantTypeSchemaAsync(TestConstants.Organization, project, View, "rst.external", AnnotationTypeCodes.Execution)).Should().BeNull();
    }

    [Fact]
    public async Task SchemaWithLocalReferences_IsSavedAndAppliedToConfigurationWrites()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();
        var configs = Context.Services.GetRequiredService<IConfigurationService>();
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        var project = "local-ref-test";
        var schema = JObject.Parse("""
            {
                "type": "object",
                "definitions": { "shortName": { "type": "string", "maxLength": 5 } },
                "properties": { "name": { "$ref": "#/definitions/shortName" } }
            }
            """);

        var stored = await schemas.SetSchemaAsync(TestConstants.Organization, project, View, AnnotationTypeCodes.Responsibility, schema, AuditActor.Account("test"), false);
        var key = await CreateResponsibilityAsync(annotations, project, "local-ref", View);

        stored.Version.Should().Be(1);
        await configs.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{ "name": "ok" }"""), AuditActor.Account("test"));
        var rejected = () => configs.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{ "name": "too long" }"""), AuditActor.Account("test"));
        (await rejected.Should().ThrowAsync<SchemaValidationException>()).Which.ValidationErrors.Should().ContainSingle()
            .Which.Errors.Should().Equal("#/name: StringTooLong");
    }

    // A schema stored before external references were refused: the writes it applies to fail until it is replaced, and
    // its URL is never fetched.
    [Fact]
    public async Task StoredSchemaWithAnExternalReference_FailsTheWritesItApplies()
    {
        var annotations = Context.Services.GetRequiredService<IAnnotationService>();
        var configs = Context.Services.GetRequiredService<IConfigurationService>();
        var schemas = Context.Services.GetRequiredService<IConfigurationSchemaService>();
        var project = "stored-external-ref";
        var actor = AuditActor.Account("test");
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var key = await CreateResponsibilityAsync(annotations, project, "stored-external", View);
        await configs.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{ "name": "before" }"""), actor);
        await PlantTypeSchemaAsync(project, AnnotationTypeCodes.Responsibility, ExternalNameSchema(listener));

        var create = () => configs.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{ "name": "after" }"""), actor);
        var patch = () => configs.PatchConfigurationAsync(key, JArray.Parse("""[{ "op": "replace", "path": "/name", "value": "patched" }]"""), actor);
        var otherLayer = () => schemas.SetAnnotationSchemaAsync(TestConstants.Organization, project, View, key.Annotation.ToString(), StrictNameSchema(), actor, false);

        (await create.Should().ThrowAsync<InvalidInputException>()).Which.ErrorCode.Should().Be(ErrorCodes.ExternalSchemaReference);
        (await patch.Should().ThrowAsync<InvalidInputException>()).Which.ErrorCode.Should().Be(ErrorCodes.ExternalSchemaReference);
        (await otherLayer.Should().ThrowAsync<InvalidInputException>()).Which.ErrorCode.Should().Be(ErrorCodes.ExternalSchemaReference);
        var forced = await configs.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{ "name": "forced" }"""), actor, true);
        forced.Content["name"]!.Value<string>().Should().Be("forced");
        listener.Pending().Should().BeFalse();
    }

    private static JObject ExternalNameSchema(TcpListener listener)
    {
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return JObject.Parse($$"""
            {
                "type": "object",
                "properties": { "name": { "$ref": "http://127.0.0.1:{{port}}/name.json" } }
            }
            """);
    }

    // A type schema as an earlier version of the service stored it: the ids of SchemaIdentity.ForType.
    private async Task PlantTypeSchemaAsync(string project, string typeCode, JObject content)
    {
        var containers = Context.Services.GetRequiredService<IContainerRepositoryProvider>();
        await containers.GetOrganizationContainer(TestConstants.Organization).Container.UpsertItemAsync(
            new ConfigurationSchemaEntity
            {
                PartitionKey = $"{project}.schema",
                Id = $"{View}.{EntityIdPrefixTypes.ConfigurationSchema}.t.{typeCode}",
                AnnotationKey = typeCode,
                Name = typeCode,
                ProjectName = project,
                ViewName = View,
                Content = content,
                Modified = AuditStamp.Of(AuditActor.System, DateTimeOffset.UtcNow),
                Version = 1,
            },
            new PartitionKey($"{project}.schema"));
    }

    private async Task<FullKey> CreateResponsibilityAsync(IAnnotationService annotations, string project, string name, string view)
    {
        var annotationKey = AnnotationKey.CreateResponsibility(name);
        await annotations.CreateAnnotationAsync(TestConstants.Organization, new Responsibility
        {
            AnnotationKey = annotationKey,
            AnnotationType = AnnotationType.Responsibility,
            Name = name,
            ProjectName = project,
            ViewName = view,
        });

        return FullKey.Create(annotationKey, TestConstants.Organization, project, view);
    }

    private static JObject StrictNameSchema()
    {
        return JObject.Parse("""
            {
                "type": "object",
                "properties": {
                    "name": { "type": "string" }
                },
                "required": ["name"],
                "additionalProperties": false
            }
            """);
    }
}
