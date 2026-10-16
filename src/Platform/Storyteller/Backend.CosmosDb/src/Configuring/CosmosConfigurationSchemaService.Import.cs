using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Entities;
using _42.Platform.Storyteller.Transferring;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace _42.Platform.Storyteller.Configuring;

// The import of a schema file. It writes the way the Set*SchemaAsync methods do (SaveAsync), after deciding the outcome
// from the view as the import leaves it: the annotations, configurations and schema layers of the overlay count as
// written, so an archive that changes a schema and its configurations together imports without Force, and a dry run
// reports what the real run does.
public partial class CosmosConfigurationSchemaService
{
    private const int MaxListedViolations = 20;

    public async Task<IReadOnlySet<SchemaKey>> GetSchemaKeysAsync(
        string organization,
        string project,
        string view,
        CancellationToken cancellationToken = default)
    {
        // current documents only ("{view}.cfs."), and an empty schema is no document, as in an export
        var prefix = $"{view}.{EntityIdPrefixTypes.ConfigurationSchema}.";
        var query = new QueryDefinition(
                "SELECT VALUE c.id FROM c WHERE STARTSWITH(c.id, @prefix) "
                + "AND IS_OBJECT(c.Content) AND ARRAY_LENGTH(ObjectToArray(c.Content)) > 0")
            .WithParameter("@prefix", prefix);

        var container = _repositoryProvider.GetOrganizationContainer(organization).Container;
        using var iterator = container.GetItemQueryIterator<string>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(GetSchemaPartitionKey(project)) });

        var keys = new HashSet<SchemaKey>();

        while (iterator.HasMoreResults)
        {
            foreach (var id in await iterator.ReadNextAsync(cancellationToken))
            {
                // the identities an import names: lower-case type codes and canonical keys
                if (ParseSuffix(id[prefix.Length..]) is { } identity && IsExportable(identity))
                {
                    keys.Add(new SchemaKey(ToDocumentKind(identity.Kind), identity.Type, identity.Key));
                }
            }
        }

        return keys;
    }

    public async Task<ViewImportResult> ImportSchemaAsync(ViewSchemaTarget target, JObject content, ViewImportOptions options, ViewImportOverlay overlay)
    {
        var identity = GetIdentity(target);
        var (organization, project, view) = (target.Organization, target.Project, target.View);

        if (identity.Kind == SchemaKind.Descendant
            && !ViewArchiveNames.CanDescend(AnnotationKey.Parse(identity.ModelKey!), identity.ModelType!))
        {
            return new ViewImportResult(
                ViewImportOutcome.Failed,
                ErrorCode: ErrorCodes.InvalidKey,
                Message: $"The type {identity.ModelType} cannot descend from {identity.ModelKey}, so it has no descendant-type schema there.");
        }

        var repository = _repositoryProvider.GetOrganizationContainer(organization);

        if (identity.Kind != SchemaKind.Type
            && await CheckAnnotationAsync(repository.Container, target, identity.ModelKey!, overlay) is { } inactive)
        {
            return inactive;
        }

        if (!content.HasValues)
        {
            return Skipped(ErrorCodes.EmptyDocument, "An empty document is not imported; delete the schema to clear it.");
        }

        try
        {
            await EnsureValidSchemaAsync(content);
        }
        catch (ArgumentException exception)
        {
            // Force never stores a broken schema
            return new ViewImportResult(ViewImportOutcome.Failed, ErrorCode: ErrorCodes.InvalidSchema, Message: exception.Message);
        }

        var partitionKey = new PartitionKey(GetSchemaPartitionKey(project));
        var existing = await ReadCurrentEntityAsync(repository, partitionKey, identity);

        // an empty schema is no document, as in an export
        var hasContent = existing?.Content.HasValues == true;

        if (hasContent && JToken.DeepEquals(existing!.Content, content))
        {
            return new ViewImportResult(ViewImportOutcome.Unchanged, existing.Version);
        }

        if (hasContent && options.Existing == ViewImportExisting.Skip)
        {
            return Skipped(
                ErrorCodes.DocumentExists,
                $"The view has a different {ViewArchiveNames.Describe(target.Kind, identity.ModelType, identity.ModelKey)}, and Skip keeps it.");
        }

        var modified = AuditStamp.Of(options.Actor, _timeProvider.GetUtcNow());
        var errors = await CollectComplianceErrorsAsync(repository, partitionKey, project, view, identity, content, modified, overlay);
        IReadOnlyList<ViewImportWarning>? warnings = null;

        if (errors.Count > 0)
        {
            var (configurations, verb) = DescribeViolations(errors);

            if (!options.Force)
            {
                return new ViewImportResult(
                    ViewImportOutcome.Failed,
                    ErrorCode: ErrorCodes.SchemaViolation,
                    Message: $"The {configurations} {verb} this schema.");
            }

            warnings =
            [
                new ViewImportWarning
                {
                    Code = ErrorCodes.SchemaViolation,
                    Message = $"Force stores this schema although the {configurations} {verb} it.",
                },
            ];
        }

        var outcome = hasContent ? ViewImportOutcome.Updated : ViewImportOutcome.Created;

        if (options.DryRun)
        {
            return new ViewImportResult(outcome, Warnings: warnings);
        }

        // checked above against the view as the import leaves it, so the write does not check again
        LogForcedViolations(organization, project, view, identity, errors);
        var written = await SaveAsync(organization, project, view, identity, content, options.Actor, force: true, isChecked: true);
        return new ViewImportResult(outcome, written.Version, Warnings: warnings);
    }

    private static SchemaIdentity GetIdentity(ViewSchemaTarget target)
    {
        return target.Kind switch
        {
            ViewDocumentKind.TypeSchema => SchemaIdentity.ForType(target.View, NormalizeAnnotationType(RequireType(target))),
            ViewDocumentKind.AnnotationSchema => SchemaIdentity.ForAnnotation(target.View, RequireKey(target)),
            ViewDocumentKind.DescendantTypeSchema => SchemaIdentity.ForDescendant(target.View, RequireKey(target), NormalizeAnnotationType(RequireType(target))),
            _ => throw new ArgumentException($"A document of the kind {target.Kind} is not a schema.", nameof(target)),
        };

        static string RequireType(ViewSchemaTarget target)
        {
            return target.AnnotationType
                   ?? throw new ArgumentException($"A schema of the kind {target.Kind} needs its type code.", nameof(target));
        }

        static string RequireKey(ViewSchemaTarget target)
        {
            return AnnotationKey.Parse(
                    target.AnnotationKey
                    ?? throw new ArgumentException($"A schema of the kind {target.Kind} needs its annotation key.", nameof(target)))
                .ToString();
        }
    }

    // The skip of a schema whose annotation is not active, neither stored nor made active by this import; null for an
    // active one.
    private static async Task<ViewImportResult?> CheckAnnotationAsync(Container container, ViewSchemaTarget target, string annotationKey, ViewImportOverlay overlay)
    {
        if (overlay.ActiveAnnotations.Contains(annotationKey))
        {
            return null;
        }

        var key = FullKey.Create(AnnotationKey.Parse(annotationKey), target.Organization, target.Project, target.View);
        var state = await AnnotationItemStore.ReadStateAsync(container, key);

        if (state == AnnotationItemState.Active)
        {
            return null;
        }

        if (overlay.FailedAnnotations.Contains(annotationKey))
        {
            return Skipped(ErrorCodes.DependencyFailed, $"The annotation {annotationKey} failed in this import.");
        }

        return state == AnnotationItemState.Deleted
            ? Skipped(ErrorCodes.AnnotationDeleted, $"The annotation {annotationKey} is deleted; restore it before importing its schemas.")
            : Skipped(ErrorCodes.AnnotationNotFound, $"The view {target.View} has no annotation {annotationKey}.");
    }

    // The configurations of the archive that the schema affects, by key, as the import writes them: with their
    // directives applied. A document whose directives fail, or which ends empty, is not written, so the stored one
    // stays.
    private static async Task<Dictionary<string, JObject>> ReadImportedConfigurationsAsync(ViewImportOverlay overlay, SchemaIdentity identity)
    {
        var documents = new Dictionary<string, JObject>(StringComparer.Ordinal);

        foreach (var (annotationKey, document) in overlay.Configurations)
        {
            if (!AnnotationKey.TryParse(annotationKey, out var key) || !IsAffected(identity, key))
            {
                continue;
            }

            try
            {
                var written = await ((JObject)document.DeepClone()).RemoveRequested().ApplyPatchRequested();

                if (written.HasValues)
                {
                    documents[annotationKey] = written;
                }
            }
            catch (Exception exception) when (exception is JsonPatchException or JsonException)
            {
                // the import of the configuration fails on its own
            }
        }

        return documents;
    }

    private static bool IsAffected(SchemaIdentity identity, AnnotationKey key)
    {
        return identity.Kind switch
        {
            SchemaKind.Type => key.TypeCode == identity.ModelType,
            SchemaKind.Annotation => key.ToString() == identity.ModelKey,
            _ => IsDescendantOf(key, identity.ModelKey!, identity.ModelType!),
        };
    }

    // ("configuration of rst.payments", "violates") for the message of a violation: the keys in order, at most 20.
    private static (string Configurations, string Verb) DescribeViolations(IReadOnlyList<SchemaValidationError> errors)
    {
        var keys = errors
            .Select(error => error.AnnotationKey)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        var listed = string.Join(", ", keys.Take(MaxListedViolations));

        if (keys.Count > MaxListedViolations)
        {
            listed += $" … and {keys.Count - MaxListedViolations} more";
        }

        return keys.Count == 1
            ? ($"configuration of {listed}", "violates")
            : ($"configurations of {listed}", "violate");
    }

    private static async Task<bool> IsValidSchemaAsync(JObject schemaContent)
    {
        try
        {
            await EnsureValidSchemaAsync(schemaContent);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static ViewImportResult Skipped(string errorCode, string message)
    {
        return new ViewImportResult(ViewImportOutcome.Skipped, ErrorCode: errorCode, Message: message);
    }

    // The schemas an import brings (ViewImportOverlay.Schemas) as layers of combined schemas. An empty or invalid one is
    // not written by the import, so the stored layer stays; each is validated once.
    private sealed class OverlaySchemaLayers(IReadOnlyDictionary<SchemaKey, JObject> schemas, AuditStamp modified)
    {
        private readonly Dictionary<SchemaKey, bool> _validity = new();

        public AuditStamp Modified => modified;

        public async Task<JObject?> GetAsync(SchemaKey key)
        {
            if (!schemas.TryGetValue(key, out var content) || !content.HasValues)
            {
                return null;
            }

            if (!_validity.TryGetValue(key, out var isValid))
            {
                isValid = await IsValidSchemaAsync(content);
                _validity[key] = isValid;
            }

            return isValid ? content : null;
        }
    }
}
