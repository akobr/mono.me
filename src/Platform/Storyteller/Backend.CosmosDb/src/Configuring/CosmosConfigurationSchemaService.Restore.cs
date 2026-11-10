using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Entities.Configurations;
using Newtonsoft.Json.Linq;
using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace _42.Platform.Storyteller.Configuring;

public partial class CosmosConfigurationSchemaService
{
    public Task<VersionRestoreResult> RestoreSchemaVersionAsync(
        string organization,
        string project,
        string view,
        string annotationType,
        uint version,
        AuditActor actor,
        bool force)
    {
        var identity = SchemaIdentity.ForType(view, NormalizeAnnotationType(annotationType));
        return RestoreAsync(organization, project, view, identity, version, actor, force);
    }

    public async Task<VersionRestoreResult> RestoreAnnotationSchemaVersionAsync(
        string organization,
        string project,
        string view,
        string annotationKey,
        uint version,
        AuditActor actor,
        bool force)
    {
        var key = AnnotationKey.Parse(annotationKey);
        await EnsureAnnotationRestorableAsync(organization, project, view, key);

        var identity = SchemaIdentity.ForAnnotation(view, key.ToString());
        return await RestoreAsync(organization, project, view, identity, version, actor, force);
    }

    public async Task<VersionRestoreResult> RestoreDescendantTypeSchemaVersionAsync(
        string organization,
        string project,
        string view,
        string annotationKey,
        string descendantTypeCode,
        uint version,
        AuditActor actor,
        bool force)
    {
        var key = AnnotationKey.Parse(annotationKey);
        var typeCode = NormalizeAnnotationType(descendantTypeCode);

        // as the write: such a layer would never apply to anything, whatever force is
        DescendantTypeRules.EnsureCanDescend(key, typeCode);
        await EnsureAnnotationRestorableAsync(organization, project, view, key);

        var identity = SchemaIdentity.ForDescendant(view, key.ToString(), typeCode);
        return await RestoreAsync(organization, project, view, identity, version, actor, force);
    }

    // The schemas of an annotation are restored only while it exists and is not deleted. Their writes do not check it,
    // but a restore follows the configuration's: a deleted annotation is restored first, a purged one is gone.
    private async Task EnsureAnnotationRestorableAsync(string organization, string project, string view, AnnotationKey key)
    {
        var container = _repositoryProvider.GetOrganizationContainer(organization).Container;

        switch (await AnnotationItemStore.ReadStateAsync(container, FullKey.Create(key, organization, project, view)))
        {
            case AnnotationItemState.Deleted:
                throw new ConflictException(
                    $"The annotation '{key}' is deleted; restore it before restoring a version of its schemas.",
                    ErrorCodes.AnnotationDeleted);

            case AnnotationItemState.Missing:
                throw new NotFoundException(
                    $"The annotation '{key}' does not exist or was deleted permanently, so no version of its schemas can be restored.",
                    ErrorCodes.AnnotationNotFound);
        }
    }

    // Stores the content of a version as a new version, also after a delete. It is checked as a write checks a schema:
    // a usable schema whatever force is, the compliance of the stored configurations unless forced. Every attempt reads
    // the schema and its state with their ETags and writes guarded by them.
    private async Task<VersionRestoreResult> RestoreAsync(
        string organization,
        string project,
        string view,
        SchemaIdentity identity,
        uint version,
        AuditActor actor,
        bool force)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = GetSchemaPartitionKey(project);
        var partitionKey = repository.Key(partitionKeyValue);
        var modified = AuditStamp.Of(actor, _timeProvider.GetUtcNow());

        // history items are written once, so the requested one is read and checked once; the schema is read again by
        // every attempt
        ConfigurationSchemaHistoryEntity? history = null;
        var isHistoryRead = false;
        var isValid = false;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var (existing, etag) = await ReadCurrentWithETagAsync(repository, partitionKey, identity);
            var (state, stateEtag) = await ReadStateAsync(repository, partitionKey, identity);

            if (existing is not null && existing.Version == version)
            {
                return VersionRestoreResult.Unchanged(version, existing.Version, existing.Modified);
            }

            if (!isHistoryRead)
            {
                history = await repository.Container.TryReadItemAsync(
                    identity.HistoryId(version),
                    partitionKey,
                    stream => stream.DeserializeNewtonsoft<ConfigurationSchemaHistoryEntity>(_serializerOptions));
                isHistoryRead = true;
            }

            if (history is null)
            {
                throw new NotFoundException(
                    $"The schema for {Describe(identity)} in view {view} has no version {version}: it was never written, or it has expired.",
                    ErrorCodes.VersionNotFound);
            }

            // an equal document writes nothing, as every write
            if (existing is not null && JToken.DeepEquals(existing.Content, history.Content))
            {
                return VersionRestoreResult.Unchanged(version, existing.Version, existing.Modified);
            }

            var content = (JObject)history.Content.DeepClone();

            if (!isValid)
            {
                // a version stored before a rule (local references only) may no longer be a usable schema
                await EnsureValidSchemaAsync(content);
                isValid = true;
            }

            await CheckComplianceAsync(organization, project, view, identity, content, modified, force, repository, partitionKey);

            var updated = await TryWriteVersionAsync(repository, partitionKeyValue, project, view, identity, existing, etag, state, stateEtag, content, modified, restoredFrom: version);

            if (updated is null)
            {
                continue;
            }

            return VersionRestoreResult.Restored(version, updated.Version, updated.Modified);
        }

        throw new SchemaConcurrencyException(project, view, identity.CurrentId, MaxRetries);
    }

    private static string Describe(SchemaIdentity identity)
    {
        return identity.Kind switch
        {
            SchemaKind.Type => identity.ModelType!,
            SchemaKind.Annotation => identity.ModelKey!,
            _ => $"{identity.ModelType} under {identity.ModelKey}",
        };
    }
}
