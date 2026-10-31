using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Annotating;
using _42.Platform.Storyteller.Entities;
using _42.Platform.Storyteller.Entities.Configurations;
using _42.Platform.Storyteller.Json;
using _42.Platform.Storyteller.Transferring;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace _42.Platform.Storyteller.Configuring;

public partial class CosmosConfigurationSchemaService : IConfigurationSchemaService
{
    private const string SchemaPartitionSuffix = "schema";
    private const int MaxRetries = 3;

    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly JsonSerializerSettings _serializerOptions;
    private readonly JsonContentDiffer _differ;
    private readonly ConfigurationSchemaChecker _checker;
    private readonly ILogger<CosmosConfigurationSchemaService> _logger;
    private readonly TimeProvider _timeProvider;

    public CosmosConfigurationSchemaService(
        IContainerRepositoryProvider repositoryProvider,
        IJsonSerializationSettingsProvider jsonSettingsProvider,
        IOptions<JsonSerializerSettings> serializerOptions,
        IOptions<ConfigurationSchemaOptions> schemaOptions,
        ILogger<CosmosConfigurationSchemaService> logger,
        TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _repositoryProvider = repositoryProvider;
        _serializerOptions = serializerOptions.Value;
        _differ = new JsonContentDiffer(jsonSettingsProvider);
        _checker = new ConfigurationSchemaChecker(schemaOptions.Value, _timeProvider);
        _logger = logger;
    }

    private enum SchemaKind
    {
        Type,
        Annotation,
        Descendant,
    }

    public async Task<IReadOnlyList<ConfigurationSchemaSummary>> ListSchemasAsync(string organization, string project, string view)
    {
        // Current schema documents only: "{view}.cfs." excludes the state ("css") and history ("csv") items.
        var prefix = $"{view}.{EntityIdPrefixTypes.ConfigurationSchema}.";
        var query = new QueryDefinition("SELECT c.id, c.Version, c.Modified FROM c WHERE STARTSWITH(c.id, @prefix)")
            .WithParameter("@prefix", prefix);

        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        using var iterator = repository.Container.GetItemQueryIterator<DocumentListingRow>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(GetSchemaPartitionKey(project)) });

        var summaries = new List<ConfigurationSchemaSummary>();

        while (iterator.HasMoreResults)
        {
            foreach (var row in await iterator.ReadNextAsync())
            {
                var summary = ToSchemaSummary(row, row.Id[prefix.Length..]);

                if (summary is not null)
                {
                    summaries.Add(summary);
                }
            }
        }

        return summaries
            .OrderBy(summary => summary.Kind)
            .ThenBy(summary => summary.AnnotationType, StringComparer.Ordinal)
            .ThenBy(summary => summary.AnnotationKey, StringComparer.Ordinal)
            .ToList();
    }

    public Task<ConfigurationSchema?> GetSchemaAsync(string organization, string project, string view, string annotationType)
    {
        var identity = SchemaIdentity.ForType(view, NormalizeAnnotationType(annotationType));
        return ReadCurrentAsync(organization, project, identity);
    }

    public Task<ConfigurationSchema> SetSchemaAsync(
        string organization,
        string project,
        string view,
        string annotationType,
        JObject schemaContent,
        AuditActor actor,
        bool force)
    {
        var identity = SchemaIdentity.ForType(view, NormalizeAnnotationType(annotationType));
        return SaveAsync(organization, project, view, identity, schemaContent, actor, force);
    }

    public Task<bool> DeleteSchemaAsync(string organization, string project, string view, string annotationType)
    {
        var identity = SchemaIdentity.ForType(view, NormalizeAnnotationType(annotationType));
        return DeleteAsync(organization, project, view, identity);
    }

    public Task<ConfigurationSchema?> GetAnnotationSchemaAsync(string organization, string project, string view, string annotationKey)
    {
        var identity = SchemaIdentity.ForAnnotation(view, AnnotationKey.Parse(annotationKey).ToString());
        return ReadCurrentAsync(organization, project, identity);
    }

    public Task<ConfigurationSchema> SetAnnotationSchemaAsync(
        string organization,
        string project,
        string view,
        string annotationKey,
        JObject schemaContent,
        AuditActor actor,
        bool force)
    {
        var identity = SchemaIdentity.ForAnnotation(view, AnnotationKey.Parse(annotationKey).ToString());
        return SaveAsync(organization, project, view, identity, schemaContent, actor, force);
    }

    public Task<bool> DeleteAnnotationSchemaAsync(string organization, string project, string view, string annotationKey)
    {
        var identity = SchemaIdentity.ForAnnotation(view, AnnotationKey.Parse(annotationKey).ToString());
        return DeleteAsync(organization, project, view, identity);
    }

    public Task<ConfigurationSchema?> GetDescendantTypeSchemaAsync(
        string organization,
        string project,
        string view,
        string annotationKey,
        string descendantTypeCode)
    {
        var identity = SchemaIdentity.ForDescendant(view, AnnotationKey.Parse(annotationKey).ToString(), NormalizeAnnotationType(descendantTypeCode));
        return ReadCurrentAsync(organization, project, identity);
    }

    public Task<ConfigurationSchema> SetDescendantTypeSchemaAsync(
        string organization,
        string project,
        string view,
        string annotationKey,
        string descendantTypeCode,
        JObject schemaContent,
        AuditActor actor,
        bool force)
    {
        var key = AnnotationKey.Parse(annotationKey);
        var typeCode = NormalizeAnnotationType(descendantTypeCode);

        // before anything is read or written, and whatever force is: such a layer would never apply to anything
        DescendantTypeRules.EnsureCanDescend(key, typeCode);

        var identity = SchemaIdentity.ForDescendant(view, key.ToString(), typeCode);
        return SaveAsync(organization, project, view, identity, schemaContent, actor, force);
    }

    public Task<bool> DeleteDescendantTypeSchemaAsync(
        string organization,
        string project,
        string view,
        string annotationKey,
        string descendantTypeCode)
    {
        var identity = SchemaIdentity.ForDescendant(view, AnnotationKey.Parse(annotationKey).ToString(), NormalizeAnnotationType(descendantTypeCode));
        return DeleteAsync(organization, project, view, identity);
    }

    public async Task<CombinedConfigurationSchema?> GetCombinedSchemaAsync(string organization, string project, string view, string annotationKey)
    {
        var parsedKey = AnnotationKey.Parse(annotationKey);
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = new PartitionKey(GetSchemaPartitionKey(project));
        return await ResolveCombinedAsync(repository, partitionKey, view, parsedKey, schemaOverride: null);
    }

    public async Task ValidateContentAsync(string organization, string project, string view, string annotationKey, JObject content, ViewImportOverlay? overlay = null)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = new PartitionKey(GetSchemaPartitionKey(project));
        var overlayLayers = overlay is null ? null : new OverlaySchemaLayers(overlay.Schemas, AuditStamp.Of(AuditActor.System, _timeProvider.GetUtcNow()), IsValidSchemaAsync);
        var combined = await ResolveCombinedAsync(repository, partitionKey, view, AnnotationKey.Parse(annotationKey), schemaOverride: null, overlayLayers: overlayLayers);

        if (combined is null)
        {
            return;
        }

        var errors = await ValidateAgainstSchemaAsync(content, combined.MergedContent);

        if (errors.Count > 0)
        {
            throw new SchemaValidationException(
            [
                new SchemaValidationError
                {
                    AnnotationKey = annotationKey,
                    ViewName = view,
                    Errors = errors,
                },
            ]);
        }
    }

    public Task<IReadOnlyCollection<ConfigurationVersion>> GetSchemaVersionsAsync(string organization, string project, string view, string annotationType)
    {
        var identity = SchemaIdentity.ForType(view, NormalizeAnnotationType(annotationType));
        return ListVersionsAsync(organization, project, identity);
    }

    public Task<ConfigurationSchema?> GetSchemaVersionContentAsync(string organization, string project, string view, string annotationType, uint version)
    {
        var identity = SchemaIdentity.ForType(view, NormalizeAnnotationType(annotationType));
        return ReadVersionAsync(organization, project, identity, version);
    }

    public Task<DiffResult> GetSchemaVersionChangesAsync(string organization, string project, string view, string annotationType, uint version)
    {
        return GetSchemaVersionChangesAsync(organization, project, view, annotationType, version == 0 ? 0 : version - 1, version);
    }

    public Task<DiffResult> GetSchemaVersionChangesAsync(string organization, string project, string view, string annotationType, uint fromVersion, uint toVersion)
    {
        var typeCode = NormalizeAnnotationType(annotationType);
        var identity = SchemaIdentity.ForType(view, typeCode);
        return DiffAsync(
            organization,
            project,
            identity,
            fromVersion,
            toVersion,
            $"Unknown version {fromVersion} of the schema for {typeCode} in view {view}.",
            $"Unknown version {toVersion} of the schema for {typeCode} in view {view}.");
    }

    public Task<IReadOnlyCollection<ConfigurationVersion>> GetAnnotationSchemaVersionsAsync(string organization, string project, string view, string annotationKey)
    {
        var identity = SchemaIdentity.ForAnnotation(view, AnnotationKey.Parse(annotationKey).ToString());
        return ListVersionsAsync(organization, project, identity);
    }

    public Task<ConfigurationSchema?> GetAnnotationSchemaVersionContentAsync(string organization, string project, string view, string annotationKey, uint version)
    {
        var identity = SchemaIdentity.ForAnnotation(view, AnnotationKey.Parse(annotationKey).ToString());
        return ReadVersionAsync(organization, project, identity, version);
    }

    public Task<DiffResult> GetAnnotationSchemaVersionChangesAsync(string organization, string project, string view, string annotationKey, uint version)
    {
        return GetAnnotationSchemaVersionChangesAsync(organization, project, view, annotationKey, version == 0 ? 0 : version - 1, version);
    }

    public Task<DiffResult> GetAnnotationSchemaVersionChangesAsync(string organization, string project, string view, string annotationKey, uint fromVersion, uint toVersion)
    {
        var key = AnnotationKey.Parse(annotationKey).ToString();
        var identity = SchemaIdentity.ForAnnotation(view, key);
        return DiffAsync(
            organization,
            project,
            identity,
            fromVersion,
            toVersion,
            $"Unknown version {fromVersion} of the schema for {key} in view {view}.",
            $"Unknown version {toVersion} of the schema for {key} in view {view}.");
    }

    public Task<IReadOnlyCollection<ConfigurationVersion>> GetDescendantTypeSchemaVersionsAsync(
        string organization,
        string project,
        string view,
        string annotationKey,
        string descendantTypeCode)
    {
        var identity = SchemaIdentity.ForDescendant(view, AnnotationKey.Parse(annotationKey).ToString(), NormalizeAnnotationType(descendantTypeCode));
        return ListVersionsAsync(organization, project, identity);
    }

    public Task<ConfigurationSchema?> GetDescendantTypeSchemaVersionContentAsync(
        string organization,
        string project,
        string view,
        string annotationKey,
        string descendantTypeCode,
        uint version)
    {
        var identity = SchemaIdentity.ForDescendant(view, AnnotationKey.Parse(annotationKey).ToString(), NormalizeAnnotationType(descendantTypeCode));
        return ReadVersionAsync(organization, project, identity, version);
    }

    public Task<DiffResult> GetDescendantTypeSchemaVersionChangesAsync(
        string organization,
        string project,
        string view,
        string annotationKey,
        string descendantTypeCode,
        uint version)
    {
        return GetDescendantTypeSchemaVersionChangesAsync(organization, project, view, annotationKey, descendantTypeCode, version == 0 ? 0 : version - 1, version);
    }

    public Task<DiffResult> GetDescendantTypeSchemaVersionChangesAsync(
        string organization,
        string project,
        string view,
        string annotationKey,
        string descendantTypeCode,
        uint fromVersion,
        uint toVersion)
    {
        var key = AnnotationKey.Parse(annotationKey).ToString();
        var typeCode = NormalizeAnnotationType(descendantTypeCode);
        var identity = SchemaIdentity.ForDescendant(view, key, typeCode);
        return DiffAsync(
            organization,
            project,
            identity,
            fromVersion,
            toVersion,
            $"Unknown version {fromVersion} of the schema for {typeCode} under {key} in view {view}.",
            $"Unknown version {toVersion} of the schema for {typeCode} under {key} in view {view}.");
    }

    public async Task<IReadOnlyList<SchemaHistory>> GetSchemaHistoriesAsync(
        string organization,
        string project,
        string view,
        CancellationToken cancellationToken = default)
    {
        // one query of the partition: "{view}.cfs.{suffix}" and "{view}.csv.{suffix}.{n}", without the state items "css"
        var currentPrefix = $"{view}.{EntityIdPrefixTypes.ConfigurationSchema}.";
        var historyPrefix = $"{view}.{EntityIdPrefixTypes.ConfigurationSchemaVersion}.";
        var query = new QueryDefinition(
                "SELECT c.id, c.Version, c.Content, c.Modified FROM c WHERE STARTSWITH(c.id, @currentPrefix) OR STARTSWITH(c.id, @historyPrefix)")
            .WithParameter("@currentPrefix", currentPrefix)
            .WithParameter("@historyPrefix", historyPrefix);

        var container = _repositoryProvider.GetOrganizationContainer(organization).Container;
        var histories = new DocumentHistories<(ViewDocumentKind Kind, string? Type, string? Key)>();
        using var iterator = container.GetItemQueryStreamIterator(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(GetSchemaPartitionKey(project)) });

        while (iterator.HasMoreResults)
        {
            using var response = await iterator.ReadNextAsync(cancellationToken);
            response.EnsureSuccessStatusCode();
            var page = response.Content.DeserializeNewtonsoft<QueryPage<DocumentArchiveRow>>(_serializerOptions);

            foreach (var row in page?.Documents ?? [])
            {
                var isCurrent = row.Id?.StartsWith(currentPrefix, StringComparison.Ordinal) == true;
                string? suffix = null;

                if (isCurrent)
                {
                    suffix = row.Id![currentPrefix.Length..];
                }
                else if (row.Id?.StartsWith(historyPrefix, StringComparison.Ordinal) == true)
                {
                    // "{suffix}.{n}"
                    var rest = row.Id[historyPrefix.Length..];
                    var versionDot = rest.LastIndexOf('.');
                    suffix = versionDot > 0 ? rest[..versionDot] : null;
                }

                if (suffix is not null && ParseSuffix(suffix) is { } identity && IsExportable(identity))
                {
                    histories.Add((ToDocumentKind(identity.Kind), identity.Type, identity.Key), row, isCurrent);
                }
            }
        }

        var documents = histories.Build().ToList();

        // annotation and descendant-type schemas follow their annotation, as configurations do: a deleted or missing
        // annotation leaves them out
        var states = await AnnotationItemStore.ReadStatesAsync(
            container,
            documents
                .Select(document => document.Identity.Key)
                .OfType<string>()
                .Select(key => FullKey.Create(AnnotationKey.Parse(key), organization, project, view)),
            cancellationToken);

        return documents
            .Where(document => document.Identity.Key is null || states.GetValueOrDefault(document.Identity.Key) == AnnotationItemState.Active)
            .OrderBy(document => document.Identity.Kind)
            .ThenBy(document => document.Identity.Type, StringComparer.Ordinal)
            .ThenBy(document => document.Identity.Key, StringComparer.Ordinal)
            .Select(document => new SchemaHistory
            {
                Kind = document.Identity.Kind,
                AnnotationType = document.Identity.Type,
                AnnotationKey = document.Identity.Key,
                Versions = document.Versions,
                Current = document.Current,
            })
            .ToList();
    }

    internal static JObject DeepMergeSchemas(IReadOnlyList<ConfigurationSchema> schemas)
    {
        if (schemas.Count == 0)
        {
            return new JObject();
        }

        var merged = (JObject)schemas[0].Content.DeepClone();

        for (var i = 1; i < schemas.Count; i++)
        {
            DeepMergeInto(merged, schemas[i].Content);
        }

        return merged;
    }

    // Legacy project-wide ids are not read. A miss on the view-scoped id is a missing schema.
    private async Task<ConfigurationSchema?> ReadCurrentAsync(string organization, string project, SchemaIdentity identity)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = new PartitionKey(GetSchemaPartitionKey(project));
        var entity = await ReadCurrentEntityAsync(repository, partitionKey, identity);
        return entity?.ToConfigurationSchema(identity.ModelType, identity.ModelKey);
    }

    // isChecked: the caller (an import) has already validated the schema and checked the compliance of the
    // configurations, so the write does not repeat it.
    private async Task<ConfigurationSchema> SaveAsync(
        string organization,
        string project,
        string view,
        SchemaIdentity identity,
        JObject schemaContent,
        AuditActor actor,
        bool force,
        bool isChecked = false)
    {
        if (!isChecked)
        {
            await EnsureValidSchemaAsync(schemaContent);
        }

        var modified = AuditStamp.Of(actor, _timeProvider.GetUtcNow());

        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = GetSchemaPartitionKey(project);
        var partitionKey = new PartitionKey(partitionKeyValue);

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var (existing, etag) = await ReadCurrentWithETagAsync(repository, partitionKey, identity);
            var (state, stateEtag) = await ReadStateAsync(repository, partitionKey, identity);

            if (existing is not null && JToken.DeepEquals(existing.Content, schemaContent))
            {
                return existing.ToConfigurationSchema(identity.ModelType, identity.ModelKey);
            }

            if (!isChecked)
            {
                IReadOnlyList<SchemaValidationError> errors;

                try
                {
                    errors = await CollectComplianceErrorsAsync(repository, partitionKey, project, view, identity, schemaContent, modified);
                }
                catch (InvalidInputException exception) when (force && ConfigurationSchemaChecker.IsCheckFailure(exception))
                {
                    // the configurations could not be checked (another stored layer refers outside itself, or a check
                    // runs out of time); force stores the schema anyway, as it stores one that they violate
                    LogForcedUncheckedCompliance(organization, project, view, identity, exception);
                    errors = [];
                }

                if (errors.Count > 0 && !force)
                {
                    throw new SchemaValidationException(errors);
                }

                LogForcedViolations(organization, project, view, identity, errors);
            }

            var version = existing is null
                ? (state?.LastVersion ?? 0) + 1
                : existing.Version + 1;

            var updated = new ConfigurationSchemaEntity
            {
                PartitionKey = partitionKeyValue,
                Id = identity.CurrentId,
                AnnotationKey = identity.AnnotationKey,
                Name = identity.Name,
                ProjectName = project,
                ViewName = view,
                Content = schemaContent,
                Modified = modified,
                Version = version,
            };

            var batch = repository.Container.CreateTransactionalBatch(partitionKey);

            if (existing is null)
            {
                batch.CreateItem(updated);
            }
            else
            {
                batch.CreateItem(existing.ToHistory(identity.HistoryId(existing.Version)));
                batch.ReplaceItem(existing.Id, updated, new TransactionalBatchItemRequestOptions { IfMatchEtag = etag });
            }

            AddStateWrite(batch, updated, identity, state, stateEtag, version);

            if (await TryExecuteWriteAsync(batch, identity.CurrentId) is null)
            {
                continue;
            }

            return updated.ToConfigurationSchema(identity.ModelType, identity.ModelKey);
        }

        throw new SchemaConcurrencyException(project, view, identity.CurrentId, MaxRetries);
    }

    // A forced write of a schema that configurations violate is logged per configuration.
    private void LogForcedViolations(string organization, string project, string view, SchemaIdentity identity, IReadOnlyList<SchemaValidationError> errors)
    {
        foreach (var error in errors)
        {
            _logger.LogWarning(
                "Schema compliance bypassed with force for {SchemaId} in {Organization}/{Project}/{View}. {AnnotationKey}: {Errors}",
                identity.CurrentId,
                organization,
                project,
                view,
                error.AnnotationKey,
                string.Join("; ", error.Errors));
        }
    }

    // A forced write of a schema whose compliance check could not finish.
    private void LogForcedUncheckedCompliance(string organization, string project, string view, SchemaIdentity identity, InvalidInputException exception)
    {
        _logger.LogWarning(
            "Schema compliance check could not finish, bypassed with force for {SchemaId} in {Organization}/{Project}/{View}. {ErrorCode}: {Message}",
            identity.CurrentId,
            organization,
            project,
            view,
            exception.ErrorCode,
            exception.Message);
    }

    private async Task<bool> DeleteAsync(string organization, string project, string view, SchemaIdentity identity)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = GetSchemaPartitionKey(project);
        var partitionKey = new PartitionKey(partitionKeyValue);

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var (existing, etag) = await ReadCurrentWithETagAsync(repository, partitionKey, identity);
            var (state, stateEtag) = await ReadStateAsync(repository, partitionKey, identity);

            if (existing is null)
            {
                return false;
            }

            var batch = repository.Container.CreateTransactionalBatch(partitionKey);
            batch.CreateItem(existing.ToHistory(identity.HistoryId(existing.Version)));
            batch.DeleteItem(identity.CurrentId, new TransactionalBatchItemRequestOptions { IfMatchEtag = etag });
            AddStateWrite(batch, existing, identity, state, stateEtag, existing.Version);

            if (await TryExecuteWriteAsync(batch, identity.CurrentId) is null)
            {
                continue;
            }

            return true;
        }

        throw new SchemaConcurrencyException(project, view, identity.CurrentId, MaxRetries);
    }

    private async Task<IReadOnlyCollection<ConfigurationVersion>> ListVersionsAsync(string organization, string project, SchemaIdentity identity)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = new PartitionKey(GetSchemaPartitionKey(project));
        var feed = repository.Container.GetItemLinqQueryable<ConfigurationSchemaHistoryEntity>(
                requestOptions: new QueryRequestOptions { PartitionKey = partitionKey })
            .Where(history => history.Id.StartsWith(identity.HistoryPrefix))
            .OrderBy(history => history.Version)
            .ToFeedIterator();

        var versions = new List<ConfigurationVersion>();

        while (feed.HasMoreResults)
        {
            var results = await feed.ReadNextAsync();
            versions.AddRange(results.Select(entity => entity.ToConfigurationVersion()));
        }

        var current = await ReadCurrentEntityAsync(repository, partitionKey, identity);

        if (current is not null)
        {
            versions.Add(current.ToConfigurationVersion());
        }

        return versions;
    }

    private async Task<ConfigurationSchema?> ReadVersionAsync(string organization, string project, SchemaIdentity identity, uint version)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = new PartitionKey(GetSchemaPartitionKey(project));
        var history = await repository.Container.TryReadItemAsync(
            identity.HistoryId(version),
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationSchemaHistoryEntity>(_serializerOptions));

        if (history is not null)
        {
            return history.ToConfigurationSchema(identity.ModelType, identity.ModelKey);
        }

        var current = await ReadCurrentEntityAsync(repository, partitionKey, identity);

        if (current is null || current.Version != version)
        {
            return null;
        }

        return current.ToConfigurationSchema(identity.ModelType, identity.ModelKey);
    }

    private Task<DiffResult> DiffAsync(
        string organization,
        string project,
        SchemaIdentity identity,
        uint fromVersion,
        uint toVersion,
        string fromErrorMessage,
        string toErrorMessage)
    {
        return _differ.GetChangesAsync(
            async () => fromVersion == 0 ? new JObject() : (await ReadVersionAsync(organization, project, identity, fromVersion))?.Content,
            async () => toVersion == 0 ? new JObject() : (await ReadVersionAsync(organization, project, identity, toVersion))?.Content,
            fromErrorMessage,
            toErrorMessage);
    }

    // The stored configurations the schema affects, validated with it in place of the stored one. With an overlay (an
    // import), they are checked as the import leaves them: an own document of the archive replaces the stored one, the
    // re-create of an annotation empties its stored one, the archive's documents of keys without a stored one are
    // checked too, and the other layers are taken from the archive where it brings them.
    private async Task<IReadOnlyList<SchemaValidationError>> CollectComplianceErrorsAsync(
        IContainerRepository repository,
        PartitionKey partitionKey,
        string project,
        string view,
        SchemaIdentity identity,
        JObject schemaContent,
        AuditStamp modified,
        ViewImportOverlay? overlay = null)
    {
        var schemaOverride = new SchemaOverride(identity, schemaContent, modified);
        var overlayLayers = overlay is null ? null : new OverlaySchemaLayers(overlay.Schemas, modified, IsValidSchemaAsync);
        var imported = overlay is null ? [] : await ReadImportedConfigurationsAsync(overlay, identity);
        var errors = new List<SchemaValidationError>();
        var layerCache = new Dictionary<string, ConfigurationSchemaEntity?>();
        var feed = QueryAffectedConfigurations(repository.Container, project, view, identity);

        while (feed.HasMoreResults)
        {
            var results = await feed.ReadNextAsync();

            foreach (var configEntity in results)
            {
                if (identity.Kind == SchemaKind.Descendant
                    && !IsDescendantOf(configEntity.AnnotationKey, identity.ModelKey!, identity.ModelType!))
                {
                    continue;
                }

                var content = configEntity.Content;

                if (imported.Remove(configEntity.AnnotationKey, out var document))
                {
                    content = document;
                }
                else if (overlay?.RecreatedAnnotations.Contains(configEntity.AnnotationKey) == true)
                {
                    continue;
                }

                await CheckAsync(configEntity.AnnotationKey, configEntity.ViewName, content);
            }
        }

        foreach (var (annotationKey, document) in imported.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            await CheckAsync(annotationKey, view, document);
        }

        return errors;

        async Task CheckAsync(string annotationKey, string viewName, JObject content)
        {
            if (!content.HasValues)
            {
                return;
            }

            var combined = await ResolveCombinedAsync(repository, partitionKey, view, AnnotationKey.Parse(annotationKey), schemaOverride, layerCache, overlayLayers);

            if (combined is null)
            {
                return;
            }

            var messages = await ValidateAgainstSchemaAsync(content, combined.MergedContent);

            if (messages.Count > 0)
            {
                errors.Add(new SchemaValidationError
                {
                    AnnotationKey = annotationKey,
                    ViewName = viewName,
                    Errors = messages,
                });
            }
        }
    }

    private async Task<CombinedConfigurationSchema?> ResolveCombinedAsync(
        IContainerRepository repository,
        PartitionKey partitionKey,
        string view,
        AnnotationKey key,
        SchemaOverride? schemaOverride,
        Dictionary<string, ConfigurationSchemaEntity?>? layerCache = null,
        OverlaySchemaLayers? overlayLayers = null)
    {
        // the layers from the most general: the type, the descendant types below the ancestors, the annotation
        var identities = new List<SchemaIdentity> { SchemaIdentity.ForType(view, key.TypeCode) };
        identities.AddRange(AnnotationHierarchy.GetAncestorSchemaSources(key)
            .Select(source => SchemaIdentity.ForDescendant(view, source.Ancestor.ToString(), source.DescendantTypeCode)));
        identities.Add(SchemaIdentity.ForAnnotation(view, key.ToString()));

        var applied = new List<ConfigurationSchema>();

        foreach (var identity in identities)
        {
            if (await ResolveLayerAsync(repository, partitionKey, identity, schemaOverride, layerCache, overlayLayers) is { } layer)
            {
                applied.Add(layer);
            }
        }

        if (applied.Count == 0)
        {
            return null;
        }

        return new CombinedConfigurationSchema
        {
            View = view,
            AnnotationKey = key.ToString(),
            MergedContent = DeepMergeSchemas(applied),
            AppliedSchemas = applied,
        };
    }

    // One layer: the schema being saved, else the one an import brings, else the stored one.
    private async Task<ConfigurationSchema?> ResolveLayerAsync(
        IContainerRepository repository,
        PartitionKey partitionKey,
        SchemaIdentity identity,
        SchemaOverride? schemaOverride,
        Dictionary<string, ConfigurationSchemaEntity?>? layerCache,
        OverlaySchemaLayers? overlayLayers)
    {
        if (schemaOverride is not null && schemaOverride.Identity.CurrentId == identity.CurrentId)
        {
            return Candidate(identity, schemaOverride.Content, schemaOverride.Modified);
        }

        if (overlayLayers is not null && await overlayLayers.GetAsync(identity.ToSchemaKey()) is { } imported)
        {
            return Candidate(identity, imported, overlayLayers.Modified);
        }

        var stored = await ReadLayerAsync(repository, partitionKey, identity, layerCache);
        return stored?.ToConfigurationSchema(identity.ModelType, identity.ModelKey);
    }

    private Task<(ConfigurationSchemaEntity? Item, string? ETag)> ReadCurrentWithETagAsync(
        IContainerRepository repository,
        PartitionKey partitionKey,
        SchemaIdentity identity)
    {
        return repository.Container.TryReadItemWithETagAsync(
            identity.CurrentId,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationSchemaEntity>(_serializerOptions));
    }

    private async Task<ConfigurationSchemaEntity?> ReadLayerAsync(
        IContainerRepository repository,
        PartitionKey partitionKey,
        SchemaIdentity identity,
        Dictionary<string, ConfigurationSchemaEntity?>? layerCache)
    {
        if (layerCache is not null && layerCache.TryGetValue(identity.CurrentId, out var cached))
        {
            return cached;
        }

        var stored = await ReadCurrentEntityAsync(repository, partitionKey, identity);

        if (layerCache is not null)
        {
            layerCache[identity.CurrentId] = stored;
        }

        return stored;
    }

    private async Task<ConfigurationSchemaEntity?> ReadCurrentEntityAsync(
        IContainerRepository repository,
        PartitionKey partitionKey,
        SchemaIdentity identity)
    {
        return await repository.Container.TryReadItemAsync(
            identity.CurrentId,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationSchemaEntity>(_serializerOptions));
    }

    private Task<(ConfigurationSchemaStateEntity? Item, string? ETag)> ReadStateAsync(
        IContainerRepository repository,
        PartitionKey partitionKey,
        SchemaIdentity identity)
    {
        return repository.Container.TryReadItemWithETagAsync(
            identity.StateId,
            partitionKey,
            stream => stream.DeserializeNewtonsoft<ConfigurationSchemaStateEntity>(_serializerOptions));
    }

    private static FeedIterator<ConfigurationEntity> QueryAffectedConfigurations(Container container, string project, string view, SchemaIdentity identity)
    {
        var query = container.GetItemLinqQueryable<ConfigurationEntity>(allowSynchronousQueryExecution: false);

        if (identity.Kind == SchemaKind.Annotation)
        {
            var id = $"{view}.{EntityIdPrefixTypes.Configuration}.{identity.AnnotationKey}";
            return query
                .Where(config => config.ProjectName == project && config.Id == id)
                .ToFeedIterator();
        }

        var prefix = $"{view}.{EntityIdPrefixTypes.Configuration}.{identity.ModelType}.";
        return query
            .Where(config =>
                config.ProjectName == project
                && config.ViewName == view
                && config.Id.StartsWith(prefix))
            .ToFeedIterator();
    }

    private static void AddStateWrite(
        TransactionalBatch batch,
        ConfigurationSchemaEntity schema,
        SchemaIdentity identity,
        ConfigurationSchemaStateEntity? state,
        string? stateEtag,
        ulong version)
    {
        var newState = new ConfigurationSchemaStateEntity
        {
            PartitionKey = schema.PartitionKey,
            Id = identity.StateId,
            AnnotationKey = schema.AnnotationKey,
            Name = schema.Name,
            ProjectName = schema.ProjectName,
            ViewName = schema.ViewName,
            LastVersion = Math.Max(state?.LastVersion ?? 0, version),
        };

        if (state is null)
        {
            batch.CreateItem(newState);
        }
        else
        {
            batch.ReplaceItem(newState.Id, newState, new TransactionalBatchItemRequestOptions { IfMatchEtag = stateEtag });
        }
    }

    private static async Task<string?> TryExecuteWriteAsync(TransactionalBatch batch, string id)
    {
        using var response = await batch.ExecuteAsync();

        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
        {
            return null;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new ConfigurationStorageException(
                $"Failed to write schema '{id}' with status {response.StatusCode}: {response.ErrorMessage}",
                response.StatusCode);
        }

        return response[response.Count - 1].ETag;
    }

    // A schema that cannot be read, that refers outside itself, or whose check cannot even start throws
    // InvalidInputException (InvalidSchema, ExternalSchemaReference, SchemaValidationTimeout) whatever force is.
    private async Task EnsureValidSchemaAsync(JObject schemaContent)
    {
        var schema = await _checker.ParseAsync(schemaContent);

        // a trial check of an empty object, whose violations do not matter: a schema that applies itself to the same
        // value again at its root, or that is its own allOf, is refused here instead of failing every later write
        _checker.Validate(schema, new JObject());
    }

    private async Task<IReadOnlyList<string>> ValidateAgainstSchemaAsync(JObject content, JObject schemaContent)
    {
        var schema = await _checker.ParseAsync(schemaContent);
        return _checker.Validate(schema, content);
    }

    private static bool IsDescendantOf(string annotationKey, string ancestorKey, string descendantTypeCode)
    {
        return IsDescendantOf(AnnotationKey.Parse(annotationKey), ancestorKey, descendantTypeCode);
    }

    private static bool IsDescendantOf(AnnotationKey annotationKey, string ancestorKey, string descendantTypeCode)
    {
        var ancestors = AnnotationHierarchy.GetAncestorSchemaSources(annotationKey);
        return ancestors.Any(ancestor => ancestor.Ancestor == ancestorKey && ancestor.DescendantTypeCode == descendantTypeCode);
    }

    // A layer that is not stored (yet): the schema being saved, or one an import brings.
    private static ConfigurationSchema Candidate(SchemaIdentity identity, JObject content, AuditStamp modified)
    {
        return new ConfigurationSchema
        {
            View = identity.View,
            AnnotationType = identity.ModelType,
            AnnotationKey = identity.ModelKey,
            Version = 0,
            Content = content,
            Modified = modified,
        };
    }

    private static void DeepMergeInto(JObject target, JObject source)
    {
        foreach (var property in source.Properties())
        {
            var existing = target.Property(property.Name);

            if (existing is null)
            {
                target.Add(property.Name, property.Value.DeepClone());
                continue;
            }

            if (property.Name == "required"
                && existing.Value is JArray existingArray
                && property.Value is JArray sourceArray)
            {
                var existingValues = existingArray.Select(token => token.ToString()).ToHashSet();

                foreach (var item in sourceArray)
                {
                    if (existingValues.Add(item.ToString()))
                    {
                        existingArray.Add(item.DeepClone());
                    }
                }

                continue;
            }

            if (existing.Value is JObject existingObject && property.Value is JObject sourceObject)
            {
                DeepMergeInto(existingObject, sourceObject);
                continue;
            }

            existing.Value = property.Value.DeepClone();
        }
    }

    private static string GetSchemaPartitionKey(string project)
    {
        return $"{project}.{SchemaPartitionSuffix}";
    }

    private static string NormalizeAnnotationType(string annotationType)
    {
        var normalized = annotationType.ToLowerInvariant();

        return AnnotationTypeCodes.ValidCodes.ContainsKey(normalized)
            ? normalized
            : throw new ArgumentException($"Invalid annotation type code: {annotationType}");
    }

    private static ConfigurationSchemaSummary? ToSchemaSummary(DocumentListingRow row, string suffix)
    {
        return ParseSuffix(suffix) is { } value
            ? new ConfigurationSchemaSummary
            {
                Kind = value.Kind switch
                {
                    SchemaKind.Type => ConfigurationSchemaKind.Type,
                    SchemaKind.Annotation => ConfigurationSchemaKind.Annotation,
                    _ => ConfigurationSchemaKind.DescendantType,
                },
                AnnotationType = value.Type,
                AnnotationKey = value.Key,
                Version = row.Version,
                Modified = row.Modified,
            }
            : null;
    }

    // The id suffix after "{view}.cfs." (and after "{view}.csv." without the version) is "t.{type}",
    // "a.{annotationKey}" or "d.{type}.{ancestorKey}" (SchemaIdentity).
    private static (SchemaKind Kind, string? Type, string? Key)? ParseSuffix(string suffix)
    {
        return suffix.Split('.', 3) switch
        {
            ["t", var type] => (SchemaKind.Type, type, null),
            ["a", var first, var rest] => (SchemaKind.Annotation, null, $"{first}.{rest}"),
            ["d", var type, var key] => (SchemaKind.Descendant, type, key),
            _ => null,
        };
    }

    // An identity whose file names can be written: a type code as the writes store it (lower-case) and a key in its
    // canonical form.
    private static bool IsExportable((SchemaKind Kind, string? Type, string? Key) identity)
    {
        return (identity.Type is null
                || (AnnotationTypeCodes.ValidCodes.ContainsKey(identity.Type)
                    && string.Equals(identity.Type, identity.Type.ToLowerInvariant(), StringComparison.Ordinal)))
               && (identity.Key is null
                   || (AnnotationKey.TryParse(identity.Key, out var key) && string.Equals(key.ToString(), identity.Key, StringComparison.Ordinal)));
    }

    private static ViewDocumentKind ToDocumentKind(SchemaKind kind)
    {
        return kind switch
        {
            SchemaKind.Type => ViewDocumentKind.TypeSchema,
            SchemaKind.Annotation => ViewDocumentKind.AnnotationSchema,
            _ => ViewDocumentKind.DescendantTypeSchema,
        };
    }

    private readonly record struct SchemaIdentity(
        SchemaKind Kind,
        string View,
        string Suffix,
        string AnnotationKey,
        string Name,
        string? ModelType,
        string? ModelKey)
    {
        public string CurrentId => $"{View}.{EntityIdPrefixTypes.ConfigurationSchema}.{Suffix}";

        public string StateId => $"{View}.{EntityIdPrefixTypes.ConfigurationSchemaState}.{Suffix}";

        public string HistoryPrefix => $"{View}.{EntityIdPrefixTypes.ConfigurationSchemaVersion}.{Suffix}.";

        public string HistoryId(ulong version) => $"{HistoryPrefix}{version}";

        public SchemaKey ToSchemaKey()
        {
            return Kind switch
            {
                SchemaKind.Type => SchemaKey.ForType(ModelType!),
                SchemaKind.Annotation => SchemaKey.ForAnnotation(ModelKey!),
                _ => SchemaKey.ForDescendantType(ModelKey!, ModelType!),
            };
        }

        public static SchemaIdentity ForType(string view, string typeCode)
        {
            return new SchemaIdentity(SchemaKind.Type, view, $"t.{typeCode}", typeCode, typeCode, typeCode, null);
        }

        public static SchemaIdentity ForAnnotation(string view, string annotationKey)
        {
            return new SchemaIdentity(SchemaKind.Annotation, view, $"a.{annotationKey}", annotationKey, annotationKey, null, annotationKey);
        }

        public static SchemaIdentity ForDescendant(string view, string ancestorKey, string descendantType)
        {
            return new SchemaIdentity(
                SchemaKind.Descendant,
                view,
                $"d.{descendantType}.{ancestorKey}",
                ancestorKey,
                $"dt.{descendantType}.{ancestorKey}",
                descendantType,
                ancestorKey);
        }
    }

    private sealed record SchemaOverride(SchemaIdentity Identity, JObject Content, AuditStamp Modified);
}
