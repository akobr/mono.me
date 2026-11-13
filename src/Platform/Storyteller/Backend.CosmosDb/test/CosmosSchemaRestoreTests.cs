using System;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Configuring;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The restore of a schema version (type, annotation and descendant-type schemas): a new version with the old content,
// the no-ops, a deleted schema, the checks of the content and of the stored configurations, and the annotation. Every
// test works in its own project.
public class CosmosSchemaRestoreTests(Startup startup)
    : BaseTestsClass(startup)
{
    private const string ProjectPrefix = "schema-restore";
    private const string View = Constants.DefaultViewName;
    private const string Type = AnnotationTypeCodes.Responsibility;

    private static readonly AuditActor Editor = AuditActor.Account("user_editor", "Editor");

    private static readonly JObject NameSchema = JObject.Parse("""{ "type": "object", "properties": { "name": { "type": "string" } } }""");
    private static readonly JObject CountSchema = JObject.Parse("""{ "type": "object", "properties": { "count": { "type": "integer" } } }""");

    private IConfigurationSchemaService Schemas => Context.Services.GetRequiredService<IConfigurationSchemaService>();

    private IConfigurationService Configs => Context.Services.GetRequiredService<IConfigurationService>();

    private IAnnotationService Annotations => Context.Services.GetRequiredService<IAnnotationService>();

    [Fact]
    public async Task TypeSchema_Restore_StoresTheOldVersionAsANewOne()
    {
        var project = $"{ProjectPrefix}-type";
        await SetTypeAsync(project, NameSchema);
        await SetTypeAsync(project, CountSchema);

        var result = await Schemas.RestoreSchemaVersionAsync(TestConstants.Organization, project, View, "RST", 1, Editor, false);

        result.Outcome.Should().Be(VersionRestoreOutcome.Restored);
        result.FromVersion.Should().Be(1);
        result.Version.Should().Be(3);
        result.Modified.By.Id.Should().Be(Editor.Id);
        var stored = await Schemas.GetSchemaAsync(TestConstants.Organization, project, View, Type);
        JToken.DeepEquals(stored!.Content, NameSchema).Should().BeTrue();

        var versions = await Schemas.GetSchemaVersionsAsync(TestConstants.Organization, project, View, Type);
        versions.Select(version => version.Version).Should().Equal(1u, 2u, 3u);
        versions.Select(version => version.RestoredFrom).Should().Equal(null, null, 1u);

        await SetTypeAsync(project, CountSchema);
        versions = await Schemas.GetSchemaVersionsAsync(TestConstants.Organization, project, View, Type);
        versions.Select(version => version.RestoredFrom).Should().Equal(null, null, 1u, null);
    }

    [Fact]
    public async Task TypeSchema_Restore_OfTheCurrentVersion_OrOfOneItEquals_ChangesNothing()
    {
        var project = $"{ProjectPrefix}-unchanged";
        await SetTypeAsync(project, NameSchema);
        await SetTypeAsync(project, CountSchema);
        await SetTypeAsync(project, NameSchema);

        var current = await Schemas.RestoreSchemaVersionAsync(TestConstants.Organization, project, View, Type, 3, Editor, false);
        var equal = await Schemas.RestoreSchemaVersionAsync(TestConstants.Organization, project, View, Type, 1, Editor, false);

        current.Outcome.Should().Be(VersionRestoreOutcome.Unchanged);
        current.Version.Should().Be(3);
        equal.Outcome.Should().Be(VersionRestoreOutcome.Unchanged);
        equal.Version.Should().Be(3);
        (await Schemas.GetSchemaVersionsAsync(TestConstants.Organization, project, View, Type)).Select(version => version.Version).Should().Equal(1u, 2u, 3u);
    }

    [Fact]
    public async Task TypeSchema_Restore_OfADeletedSchema_CreatesItAgain_AndAnUnknownVersionIsNotFound()
    {
        var project = $"{ProjectPrefix}-deleted";
        await SetTypeAsync(project, NameSchema);
        await SetTypeAsync(project, CountSchema);
        (await Schemas.DeleteSchemaAsync(TestConstants.Organization, project, View, Type)).Should().BeTrue();

        var result = await Schemas.RestoreSchemaVersionAsync(TestConstants.Organization, project, View, Type, 2, Editor, false);

        result.Outcome.Should().Be(VersionRestoreOutcome.Restored);
        result.Version.Should().Be(3);
        JToken.DeepEquals((await Schemas.GetSchemaAsync(TestConstants.Organization, project, View, Type))!.Content, CountSchema).Should().BeTrue();

        var unknown = () => Schemas.RestoreSchemaVersionAsync(TestConstants.Organization, project, View, Type, 8, Editor, false);
        (await unknown.Should().ThrowAsync<NotFoundException>()).Which.ErrorCode.Should().Be(ErrorCodes.VersionNotFound);
    }

    [Fact]
    public async Task TypeSchema_Restore_NeedsTheStoredConfigurationsToComply_UnlessForced()
    {
        var project = $"{ProjectPrefix}-compliance";
        var key = await CreateAsync(project, "rst.billing");
        await SetTypeAsync(project, JObject.Parse("""{ "type": "object", "required": [ "b" ] }"""));
        await Configs.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{ "a": 1, "b": 1 }"""), AuditActor.System);
        await SetTypeAsync(project, JObject.Parse("""{ "type": "object" }"""));
        await Configs.PatchConfigurationAsync(key, JArray.Parse("""[{ "op": "remove", "path": "/b" }]"""), AuditActor.System);

        var act = () => Schemas.RestoreSchemaVersionAsync(TestConstants.Organization, project, View, Type, 1, Editor, false);

        var failure = await act.Should().ThrowAsync<SchemaValidationException>();
        failure.Which.ValidationErrors.Select(error => error.AnnotationKey).Should().Equal("rst.billing");
        (await Schemas.GetSchemaVersionsAsync(TestConstants.Organization, project, View, Type)).Select(version => version.Version).Should().Equal(new[] { 1u, 2u }, "a rejected restore leaves no version");

        var forced = await Schemas.RestoreSchemaVersionAsync(TestConstants.Organization, project, View, Type, 1, Editor, true);
        forced.Outcome.Should().Be(VersionRestoreOutcome.Restored);
        forced.Version.Should().Be(3);
    }

    [Fact]
    public async Task TypeSchema_Restore_OfAVersionThatIsNoUsableSchema_IsRefused_EvenWhenForced()
    {
        var project = $"{ProjectPrefix}-unusable";
        await SetTypeAsync(project, NameSchema);
        await SetTypeAsync(project, CountSchema);

        // a version stored before the rule of local references
        var historyId = $"{View}.csv.t.{Type}.1";
        var partitionKey = OrganizationKeys.Key(TestConstants.Organization, $"{project}.schema");
        var history = (await GetContainer().ReadItemAsync<JObject>(historyId, partitionKey)).Resource;
        history["Content"] = JObject.Parse("""{ "$ref": "https://example.com/schema.json" }""");
        await GetContainer().ReplaceItemAsync(history, historyId, partitionKey);

        var act = () => Schemas.RestoreSchemaVersionAsync(TestConstants.Organization, project, View, Type, 1, Editor, true);

        (await act.Should().ThrowAsync<InvalidInputException>()).Which.ErrorCode.Should().Be(ErrorCodes.ExternalSchemaReference);
        (await Schemas.GetSchemaVersionsAsync(TestConstants.Organization, project, View, Type)).Select(version => version.Version).Should().Equal(1u, 2u);
    }

    [Fact]
    public async Task AnnotationSchema_Restore_FollowsTheAnnotation()
    {
        var project = $"{ProjectPrefix}-annotation";
        var key = await CreateAsync(project, "rst.alpha");
        await Schemas.SetAnnotationSchemaAsync(TestConstants.Organization, project, View, "rst.alpha", NameSchema, AuditActor.System, false);
        await Schemas.SetAnnotationSchemaAsync(TestConstants.Organization, project, View, "rst.alpha", CountSchema, AuditActor.System, false);

        var restored = await Schemas.RestoreAnnotationSchemaVersionAsync(TestConstants.Organization, project, View, "rst.alpha", 1, Editor, false);
        restored.Outcome.Should().Be(VersionRestoreOutcome.Restored);
        restored.Version.Should().Be(3);
        (await Schemas.GetAnnotationSchemaVersionsAsync(TestConstants.Organization, project, View, "rst.alpha")).Last().RestoredFrom.Should().Be(1);

        await Annotations.DeleteAnnotationAsync(key);
        var deleted = () => Schemas.RestoreAnnotationSchemaVersionAsync(TestConstants.Organization, project, View, "rst.alpha", 2, Editor, false);
        (await deleted.Should().ThrowAsync<ConflictException>()).Which.ErrorCode.Should().Be(ErrorCodes.AnnotationDeleted);

        await Annotations.PurgeAnnotationAsync(key, AnnotationServiceTestExtensions.TestActor);
        var purged = () => Schemas.RestoreAnnotationSchemaVersionAsync(TestConstants.Organization, project, View, "rst.alpha", 2, Editor, false);
        (await purged.Should().ThrowAsync<NotFoundException>()).Which.ErrorCode.Should().Be(ErrorCodes.AnnotationNotFound);

        var unknown = () => Schemas.RestoreAnnotationSchemaVersionAsync(TestConstants.Organization, project, View, "rst.never", 1, Editor, false);
        (await unknown.Should().ThrowAsync<NotFoundException>()).Which.ErrorCode.Should().Be(ErrorCodes.AnnotationNotFound);
    }

    [Fact]
    public async Task DescendantTypeSchema_Restore_ChecksThePairAndTheAnnotation()
    {
        var project = $"{ProjectPrefix}-descendant";
        await CreateAsync(project, "rst.beta");
        await Schemas.SetDescendantTypeSchemaAsync(TestConstants.Organization, project, View, "rst.beta", AnnotationTypeCodes.Unit, NameSchema, AuditActor.System, false);
        await Schemas.SetDescendantTypeSchemaAsync(TestConstants.Organization, project, View, "rst.beta", AnnotationTypeCodes.Unit, CountSchema, AuditActor.System, false);

        var restored = await Schemas.RestoreDescendantTypeSchemaVersionAsync(TestConstants.Organization, project, View, "rst.beta", "UNT", 1, Editor, false);
        restored.Outcome.Should().Be(VersionRestoreOutcome.Restored);
        restored.Version.Should().Be(3);
        JToken.DeepEquals((await Schemas.GetDescendantTypeSchemaAsync(TestConstants.Organization, project, View, "rst.beta", AnnotationTypeCodes.Unit))!.Content, NameSchema).Should().BeTrue();

        var impossible = () => Schemas.RestoreDescendantTypeSchemaVersionAsync(TestConstants.Organization, project, View, "rst.beta", AnnotationTypeCodes.Subject, 1, Editor, true);
        (await impossible.Should().ThrowAsync<InvalidInputException>()).Which.ErrorCode.Should().Be(ErrorCodes.InvalidKey);

        var missing = () => Schemas.RestoreDescendantTypeSchemaVersionAsync(TestConstants.Organization, project, View, "rst.gamma", AnnotationTypeCodes.Unit, 1, Editor, false);
        (await missing.Should().ThrowAsync<NotFoundException>()).Which.ErrorCode.Should().Be(ErrorCodes.AnnotationNotFound);
    }

    // The first attempt passes the compliance check; before its batch, a configuration changes so that it breaks the
    // restored version, and the batch loses to a concurrent change of the schema. The retry checks again and refuses.
    [Fact]
    public async Task TypeSchema_Restore_ChecksTheConfigurationsAgainOnARetry()
    {
        var project = $"{ProjectPrefix}-recheck";
        var key = await CreateAsync(project, "rst.recheck");
        await SetTypeAsync(project, NameSchema);
        await SetTypeAsync(project, CountSchema);
        await Configs.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{ "name": "ok" }"""), AuditActor.System);
        var storage = new InterceptedRepositoryProvider(Context.Services.GetRequiredService<IContainerRepositoryProvider>());
        var schemas = ActivatorUtilities.CreateInstance<CosmosConfigurationSchemaService>(Context.Services, storage);
        var id = $"{View}.cfs.t.{Type}";
        var changed = false;

        storage.Interceptor.BeforeBatch = async batch =>
        {
            if (batch.Contains(BatchOperationKind.Replace, id) && !changed)
            {
                changed = true;

                // the current schema (count) allows it, version 1 (name as a string) does not
                await Configs.CreateOrUpdateConfigurationAsync(key, JObject.Parse("""{ "name": 5 }"""), AuditActor.System);
                await TouchAsync(project, id);
            }

            return null;
        };

        var act = () => schemas.RestoreSchemaVersionAsync(TestConstants.Organization, project, View, Type, 1, Editor, false);

        var failure = await act.Should().ThrowAsync<SchemaValidationException>();
        changed.Should().BeTrue();
        failure.Which.ValidationErrors.Select(error => error.AnnotationKey).Should().Equal("rst.recheck");
        (await Schemas.GetSchemaVersionsAsync(TestConstants.Organization, project, View, Type)).Select(version => version.Version).Should().Equal(new[] { 1u, 2u }, "the refused retry stores no version");
    }

    [Fact]
    public async Task TypeSchema_Restore_RetriesAConcurrentChange_AndGivesUpAfterThreeMore()
    {
        var project = $"{ProjectPrefix}-concurrent";
        await SetTypeAsync(project, NameSchema);
        await SetTypeAsync(project, CountSchema);
        var storage = new InterceptedRepositoryProvider(Context.Services.GetRequiredService<IContainerRepositoryProvider>());
        var schemas = ActivatorUtilities.CreateInstance<CosmosConfigurationSchemaService>(Context.Services, storage);
        var id = $"{View}.cfs.t.{Type}";
        var changes = 0;
        var always = false;

        storage.Interceptor.BeforeBatch = async batch =>
        {
            if (batch.Contains(BatchOperationKind.Replace, id) && (always || changes++ == 0))
            {
                await TouchAsync(project, id);
            }

            return null;
        };

        var restored = await schemas.RestoreSchemaVersionAsync(TestConstants.Organization, project, View, Type, 1, Editor, false);

        changes.Should().Be(2, "the first attempt lost to the concurrent change, the second wrote");
        restored.Version.Should().Be(3);

        always = true;
        var act = () => schemas.RestoreSchemaVersionAsync(TestConstants.Organization, project, View, Type, 2, Editor, false);

        await act.Should().ThrowAsync<SchemaConcurrencyException>();
        (await Schemas.GetSchemaAsync(TestConstants.Organization, project, View, Type))!.Version.Should().Be(3);
    }

    // A change of the item that leaves its document as it is.
    private async Task TouchAsync(string project, string id)
    {
        using var response = await GetContainer().PatchItemStreamAsync(
            id,
            OrganizationKeys.Key(TestConstants.Organization, $"{project}.schema"),
            [PatchOperation.Set("/touched", Guid.NewGuid().ToString("N"))]);

        response.IsSuccessStatusCode.Should().BeTrue();
    }

    private Task SetTypeAsync(string project, JObject schema)
    {
        return Schemas.SetSchemaAsync(TestConstants.Organization, project, View, Type, schema, AuditActor.System, false);
    }

    private async Task<FullKey> CreateAsync(string project, string key)
    {
        await Annotations.CreateAnnotationAsync(TestConstants.Organization, AnnotationFactory.CreateDefault(AnnotationKey.Parse(key), project, View));
        return FullKey.Create(AnnotationKey.Parse(key), TestConstants.Organization, project, View);
    }

    private Container GetContainer()
    {
        return Context.Services
            .GetRequiredService<IContainerRepositoryProvider>()
            .GetOrganizationContainer(TestConstants.Organization)
            .Container;
    }
}
