using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Entities.Configurations;
using Newtonsoft.Json.Linq;
using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace _42.Platform.Storyteller.Configuring;

public partial class CosmosConfigurationService
{
    // Stores the content of a listed version as a new version, whole and as stored: no merge, and a stored "$remove" or
    // "$patch" stays a property. Every attempt reads the row with its ETag and writes guarded by it, as a write does.
    public async Task<VersionRestoreResult> RestoreConfigurationVersionAsync(FullKey key, uint version, AuditActor actor, bool force = false)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(key.OrganizationName);
        var annotationKey = key.Annotation.ToString();
        var id = GetConfigurationId(key);
        var partitionKey = key.GetCosmosPartitionKey();

        // a soft-deleted annotation hides its versions, so it answers before any version is looked up; a purged one
        // keeps its versions until they expire, but nothing to restore them to
        switch (await AnnotationItemStore.ReadStateAsync(repository.Container, key))
        {
            case AnnotationItemState.Deleted:
                throw new ConflictException(
                    $"The annotation '{annotationKey}' is deleted; restore it before restoring a version of its configuration.",
                    ErrorCodes.AnnotationDeleted);

            case AnnotationItemState.Missing:
                throw new NotFoundException(
                    $"The annotation '{annotationKey}' does not exist or was deleted permanently, so no version of its configuration can be restored.",
                    ErrorCodes.AnnotationNotFound);
        }

        // archives are written once, so the requested one is read once; the row is read again by every attempt
        ConfigurationHistoryEntity? archive = null;
        var isArchiveRead = false;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var (existing, etag) = await ReadForWriteAsync(key, repository, id, partitionKey);

            // a cache row (version 0, no content) only caches the effective document: there is no current version
            var current = existing is not null && IsWrittenVersion(existing) ? existing : null;

            if (current is not null && current.Version == version)
            {
                return VersionRestoreResult.Unchanged(version, current.Version, current.Modified);
            }

            // as the version list: an archive of the current or a later version is a leftover, not a version
            if (current is { Version: > 0 } && version > current.Version)
            {
                throw VersionNotFound(annotationKey, version);
            }

            if (!isArchiveRead)
            {
                archive = await ReadHistoryAsync(key, repository, version);
                isArchiveRead = true;
            }

            if (archive is null || !IsWrittenVersion(archive.Version, archive.Content))
            {
                throw VersionNotFound(annotationKey, version);
            }

            // an equal document writes nothing, as every write
            if (current is not null && JToken.DeepEquals(current.Content, archive.Content))
            {
                return VersionRestoreResult.Unchanged(version, current.Version, current.Modified);
            }

            var content = (JObject)archive.Content.DeepClone();

            // validate before any write, so a rejection cannot leave a history item; every attempt checks again, as the
            // schema may have changed since the attempt that lost
            if (!force && _schemaService is not null && content.HasValues)
            {
                await _schemaService.ValidateContentAsync(key.OrganizationName, key.ProjectName, key.ViewName, annotationKey, content);
            }

            // a cleared configuration has no row; one that only caches a calculation has a cache row
            var written = existing is null
                ? await TryCreateVersionAsync(key, repository, content, actor, restoredFrom: version)
                : await TryWriteNextVersionAsync(key, repository, existing, etag!, content, actor, restoredFrom: version);

            if (written is null)
            {
                continue;
            }

            await AuditConfigurationAsync(key, repository, actor, AnnotationAuditAction.ConfigurationRestored, written.Version, fromVersion: version);
            await CompleteInvalidationAsync(key, repository, written.PendingInvalidationId);
            return VersionRestoreResult.Restored(version, written.Version, written.Modified);
        }

        throw ConcurrentChange(annotationKey);
    }

    private static NotFoundException VersionNotFound(string annotationKey, uint version)
    {
        return new NotFoundException(
            $"The configuration of '{annotationKey}' has no version {version}: it was never written, or it has expired.",
            ErrorCodes.VersionNotFound);
    }
}
