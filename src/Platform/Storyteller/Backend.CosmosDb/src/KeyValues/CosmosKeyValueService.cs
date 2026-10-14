using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Configuring;
using _42.Platform.Storyteller.Entities;
using _42.Platform.Storyteller.Entities.KeyValues;
using _42.Platform.Storyteller.Json;
using _42.Platform.Storyteller.KeyValues.Audit;
using _42.Platform.Storyteller.KeyValues.Policy;
using _42.Platform.Storyteller.KeyValues.Protection;
using _42.Platform.Storyteller.KeyValues.Usage;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using PartitionKey = Microsoft.Azure.Cosmos.PartitionKey;

namespace _42.Platform.Storyteller.KeyValues;

public class CosmosKeyValueService : IKeyValueService
{
    // writers of one key retry with a short random pause, so a burst of them still gets consecutive versions
    private const int MaxRetries = 8;
    private const int MaxBatchOperations = 100;
    private const int MaxImportBatchKeys = MaxBatchOperations / 2;
    private const int MaxImportItems = 1000;
    private const int MaxBulkKeys = 100;
    private const int MaxBulkPrefixKeys = 1000;
    private const int MaxParallelProtections = 8;

    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly IValueProtector _protector;
    private readonly IKeyValuePolicyStore _policies;
    private readonly IKeyValueAuditLog? _audit;
    private readonly IKeyValueUsageRecorder? _usage;
    private readonly KeyValueOptions _options;
    private readonly JsonSerializerSettings _serializerOptions;
    private readonly JsonContentDiffer _differ;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CosmosKeyValueService> _logger;

    public CosmosKeyValueService(
        IContainerRepositoryProvider repositoryProvider,
        IValueProtector protector,
        IKeyValuePolicyStore policies,
        IOptions<KeyValueOptions> options,
        IJsonSerializationSettingsProvider jsonSettingsProvider,
        IOptions<JsonSerializerSettings> serializerOptions,
        IKeyValueAuditLog? audit = null,
        IKeyValueUsageRecorder? usage = null,
        TimeProvider? timeProvider = null,
        ILogger<CosmosKeyValueService>? logger = null)
    {
        _repositoryProvider = repositoryProvider;
        _protector = protector;
        _policies = policies;
        _audit = audit;
        _usage = usage;
        _options = options.Value;
        _serializerOptions = serializerOptions.Value;
        _differ = new JsonContentDiffer(jsonSettingsProvider);
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<CosmosKeyValueService>.Instance;
    }

    public async Task<KeyValuesResponse> ListAsync(string organization, string project, string view, KeyValueListRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var place = GetPlace(organization, project, view);
        var query = BuildListingQuery(request);

        using var iterator = place.Container.GetItemQueryIterator<KeyValueListingRow>(
            query,
            string.IsNullOrWhiteSpace(request.ContinuationToken) ? null : request.ContinuationToken,
            new QueryRequestOptions { PartitionKey = place.PartitionKey, MaxItemCount = CosmosConstants.MaxItemCountPerPage });

        if (!iterator.HasMoreResults)
        {
            return new KeyValuesResponse { Items = [] };
        }

        var page = await iterator.ReadNextAsync();
        LogCharge("list", place, page.RequestCharge);
        _usage?.Add(place.Organization, place.Project, KeyValueUsageMeters.ReadsApi);
        var now = _timeProvider.GetUtcNow();
        var items = page.Select(row => row.ToSummary(now)).ToList();

        return new KeyValuesResponse
        {
            Items = items,
            ContinuationToken = page.ContinuationToken,
            Count = items.Count,
        };
    }

    public async Task<KeyValue?> GetAsync(string organization, string project, string view, string key, KeyValueReadOptions options, KeyValueCaller caller)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(caller);
        key = KeyValueRules.EnsureKey(key);
        var place = GetPlace(organization, project, view);
        var (entity, _) = await ReadAsync(place, key);

        if (entity is null || (entity.IsDeleted && !options.IncludeDeleted))
        {
            return null;
        }

        var value = await ResolveValueAsync(place, entity, options.Reveal, caller);
        _usage?.Add(place.Organization, place.Project, KeyValueUsageMeters.ReadsApi);

        if (value is not null && entity.Content.Protection.IsSecret())
        {
            await RecordRevealAsync(place, caller, entity.Key, entity.Version, entity.Content.Protection, KeyValueAuditOperation.Reveal);
        }

        return entity.ToKeyValue(value, _timeProvider.GetUtcNow());
    }

    public async Task<KeyValue> SetAsync(string organization, string project, string view, string key, KeyValueWrite write, KeyValueCaller caller)
    {
        ArgumentNullException.ThrowIfNull(write);
        ArgumentNullException.ThrowIfNull(caller);
        key = KeyValueRules.EnsureKey(key);
        KeyValueRules.EnsureContentType(write.ContentType);
        var place = GetPlace(organization, project, view);
        var normalizedKey = KeyValueRules.Normalize(key);
        var policy = await _policies.GetAsync(organization, project);

        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            await BackOffAsync(attempt);
            var now = _timeProvider.GetUtcNow();
            var (existing, etag) = await ReadAsync(place, key);

            if (existing is null)
            {
                var protection = write.Protection ?? throw KeyValueErrors.ProtectionRequired(key);
                EnsureProtectionAllowed(policy, protection);
                KeyValueRules.EnsureValue(write.Value, protection);
                EnsureReference(protection, write.Value, write.ContentType);

                if (protection.IsSecret() && !caller.CanWriteSecrets)
                {
                    throw KeyValueErrors.SecretRightsRequired(key);
                }

                if (write.ExpectedVersion is > 0)
                {
                    throw KeyValueErrors.VersionMismatch(key, write.ExpectedVersion.Value, 0);
                }

                // numbering continues after the versions of an expired deleted key
                var version = await GetNextVersionAsync(place, normalizedKey);
                var content = await _protector.ProtectAsync(place.Identity(normalizedKey, version), protection, write.Value, write.ContentType);
                var created = new KeyValueEntity
                {
                    PartitionKey = place.PartitionKeyValue,
                    Id = GetId(normalizedKey),
                    ProjectName = project,
                    ViewName = view,
                    Key = key,
                    Version = version,
                    Content = content,
                    Author = caller.Author,
                    UpdatedAt = now,
                    CreatedBy = caller.Author,
                    CreatedAt = now,
                };

                try
                {
                    var response = await place.Container.CreateItemAsync(created, place.PartitionKey);
                    LogCharge("create", place, response.RequestCharge);
                    await RecordWriteAsync(place, caller, created, KeyValueAuditOperation.Create, previous: null);
                    return created.ToKeyValue(write.Value, now);
                }
                catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
                {
                    // created concurrently, the next attempt updates it
                    continue;
                }
            }

            var targetProtection = write.Protection ?? existing.Content.Protection;
            EnsureProtectionAllowed(policy, targetProtection);
            KeyValueRules.EnsureValue(write.Value, targetProtection);
            EnsureReference(targetProtection, write.Value, write.ContentType);
            EnsureSecretWriteRights(existing, targetProtection, caller);
            EnsureExpectedVersion(existing, write.ExpectedVersion);

            if (!existing.IsDeleted
                && targetProtection == existing.Content.Protection
                && string.Equals(write.ContentType, existing.Content.ContentType, StringComparison.Ordinal)
                && await IsSameValueAsync(place, existing, write.Value))
            {
                return existing.ToKeyValue(write.Value, now);
            }

            var newContent = await _protector.ProtectAsync(place.Identity(normalizedKey, existing.Version + 1), targetProtection, write.Value, write.ContentType);
            var updated = await TryWriteNextVersionAsync(place, existing, etag!, newContent, caller.Author, now);

            if (updated is not null)
            {
                await RecordWriteAsync(place, caller, updated, KeyValueAuditOperation.Update, existing.Content.Protection);
                return updated.ToKeyValue(write.Value, now);
            }
        }

        throw KeyValueErrors.Busy(view, key, MaxRetries);
    }

    public async Task<bool> DeleteAsync(string organization, string project, string view, string key, KeyValueDeleteOptions options, KeyValueCaller caller)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(caller);
        key = KeyValueRules.EnsureKey(key);
        var place = GetPlace(organization, project, view);

        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            await BackOffAsync(attempt);
            var (existing, etag) = await ReadAsync(place, key);

            if (existing is null || (existing.IsDeleted && !options.Permanent))
            {
                return false;
            }

            EnsureSecretWriteRights(existing, existing.Content.Protection, caller);
            EnsureExpectedVersion(existing, options.ExpectedVersion);

            if (options.Permanent)
            {
                await PermanentlyDeleteAsync(place, existing, etag!);
                await RecordWriteAsync(place, caller, existing, KeyValueAuditOperation.PermanentDelete, previous: null);
                return true;
            }

            if (await TrySoftDeleteAsync(place, existing, etag!, caller.Author))
            {
                await RecordWriteAsync(place, caller, existing, KeyValueAuditOperation.Delete, previous: null);
                return true;
            }
        }

        throw KeyValueErrors.Busy(view, key, MaxRetries);
    }

    public Task<KeyValueBulkResult> RestoreAsync(string organization, string project, string view, KeyValueBulkRequest request, KeyValueCaller caller)
    {
        return RunBulkAsync(organization, project, view, request, caller, KeyValueState.Deleted, RestoreOneAsync);
    }

    public Task<KeyValueBulkResult> PurgeAsync(string organization, string project, string view, KeyValueBulkRequest request, KeyValueCaller caller)
    {
        return RunBulkAsync(organization, project, view, request, caller, KeyValueState.Deleted, PurgeOneAsync);
    }

    public async Task<KeyValueBulkResult> ProtectAsync(string organization, string project, string view, KeyValueBulkRequest request, KeyValueCaller caller)
    {
        ArgumentNullException.ThrowIfNull(request);
        var target = request.Protection
            ?? throw new InvalidInputException("The target protection level is required.", ErrorCodes.ProtectionRequired);
        var policy = await _policies.GetAsync(organization, project);
        EnsureProtectionAllowed(policy, target);

        return await RunBulkAsync(organization, project, view, request, caller, KeyValueState.Active, (place, key, bulkCaller) => ProtectOneAsync(place, key, target, bulkCaller));
    }

    public async Task<IReadOnlyList<KeyValueVersion>> GetVersionsAsync(string organization, string project, string view, string key)
    {
        key = KeyValueRules.EnsureKey(key);
        var place = GetPlace(organization, project, view);
        var normalizedKey = KeyValueRules.Normalize(key);
        var now = _timeProvider.GetUtcNow();

        var query = new QueryDefinition(
                "SELECT c.Version, c.Author, c.CreationTime, c.Content.Protection AS Protection, c.ttl, c._ts "
                + "FROM c WHERE STARTSWITH(c.id, @prefix) ORDER BY c.Version")
            .WithParameter("@prefix", GetVersionIdPrefix(normalizedKey));

        var versions = new List<KeyValueVersion>();
        using var iterator = place.Container.GetItemQueryIterator<KeyValueVersionRow>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = place.PartitionKey });

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync();
            LogCharge("versions", place, page.RequestCharge);
            versions.AddRange(page.Select(row => row.ToVersion(now)));
        }

        var (entity, _) = await ReadAsync(place, key);

        if (entity is not null)
        {
            var index = versions.FindIndex(version => version.Version == entity.Version);

            if (index >= 0)
            {
                // the delete wrote the history item of the current version
                versions[index] = versions[index] with { IsCurrent = true };
            }
            else
            {
                versions.Add(entity.ToCurrentVersion(now));
            }
        }

        return versions.OrderBy(version => version.Version).ToList();
    }

    public async Task<KeyValue?> GetVersionAsync(string organization, string project, string view, string key, ulong version, bool reveal, KeyValueCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        key = KeyValueRules.EnsureKey(key);
        var place = GetPlace(organization, project, view);
        var (catalog, _) = await ReadAsync(place, key);

        if (catalog is not null && catalog.Version == version)
        {
            var currentValue = await ResolveValueAsync(place, catalog, reveal, caller);
            return catalog.ToKeyValue(currentValue, _timeProvider.GetUtcNow());
        }

        var item = await ReadVersionAsync(place, KeyValueRules.Normalize(key), version);

        if (item is null)
        {
            return null;
        }

        var value = await ResolveValueAsync(place, item.Key, item.Version, item.Content, reveal, caller);
        return item.ToKeyValue(catalog, value);
    }

    public async Task<DiffResult> GetVersionChangesAsync(string organization, string project, string view, string key, ulong fromVersion, ulong toVersion, KeyValueCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        key = KeyValueRules.EnsureKey(key);
        var place = GetPlace(organization, project, view);

        var fromText = await GetVersionTextAsync(place, key, fromVersion, caller)
            ?? throw new InvalidOperationException($"Unknown version {fromVersion} of the key '{key}' in view '{view}'.");
        var toText = await GetVersionTextAsync(place, key, toVersion, caller)
            ?? throw new InvalidOperationException($"Unknown version {toVersion} of the key '{key}' in view '{view}'.");

        return _differ.GetTextChanges(fromText, toText);
    }

    public async Task<KeyValueDocument> ExportAsync(string organization, string project, string view, KeyValueExportRequest request, KeyValueCaller caller)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(caller);

        if (request.IncludeSecrets && !caller.CanReadSecrets)
        {
            throw new AccessDeniedException("The caller has no rights to export secrets.");
        }

        var place = GetPlace(organization, project, view);
        var entities = await QueryEntitiesAsync(place, KeyValueState.Active, request.KeyPrefix, request.Protections);
        var values = new string?[entities.Count];

        using var throttler = new SemaphoreSlim(MaxParallelProtections);
        await Task.WhenAll(entities.Select(async (entity, index) =>
        {
            if (!entity.Content.Protection.IsSecret())
            {
                values[index] = entity.Content.Value;
                return;
            }

            if (!request.IncludeSecrets)
            {
                return;
            }

            await throttler.WaitAsync();

            try
            {
                values[index] = await _protector.UnprotectAsync(place.Identity(entity), entity.Content);
            }
            finally
            {
                throttler.Release();
            }
        }));

        _usage?.Add(place.Organization, place.Project, KeyValueUsageMeters.ReadsApi);
        var revealedSecrets = entities.Count(entity => entity.Content.Protection.IsSecret() && request.IncludeSecrets);

        if (revealedSecrets > 0)
        {
            _usage?.Add(place.Organization, place.Project, KeyValueUsageMeters.Reveals, revealedSecrets);
            await AuditAsync(place, caller, KeyValueAuditOperation.Export, key: null, version: null, protection: null, $"{revealedSecrets} secret value(s)");
        }

        return new KeyValueDocument
        {
            Items = entities.Select((entity, index) => new KeyValueDocumentItem
            {
                Key = entity.Key,
                Protection = entity.Content.Protection,
                Value = values[index],
                ContentType = entity.Content.ContentType,
            }).ToList(),
        };
    }

    public async Task<KeyValueImportResult> ImportAsync(string organization, string project, string view, KeyValueDocument document, KeyValueImportOptions options, KeyValueCaller caller)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(caller);

        if (!string.Equals(document.Format, KeyValueDocument.CurrentFormat, StringComparison.Ordinal))
        {
            throw new InvalidInputException($"The import format '{document.Format}' is not supported; use '{KeyValueDocument.CurrentFormat}'.", ErrorCodes.ImportInvalid);
        }

        if (document.Items.Count > MaxImportItems)
        {
            throw new InvalidInputException($"An import may have at most {MaxImportItems} items.", ErrorCodes.ImportInvalid);
        }

        var place = GetPlace(organization, project, view);
        var policy = await _policies.GetAsync(organization, project);
        var normalizedPrefix = string.IsNullOrWhiteSpace(options.KeyPrefix) ? null : KeyValueRules.Normalize(options.KeyPrefix.Trim());
        var errors = ValidateImportItems(document, options, normalizedPrefix, policy);
        var invalidKeys = errors.Select(error => error.Key).ToHashSet(StringComparer.Ordinal);

        // a caller without secret rights submits no secret values, and never makes the plan decrypt existing secrets
        if (!caller.CanWriteSecrets && document.Items.Any(item => item.Protection.IsSecret() && item.Value is not null))
        {
            throw new AccessDeniedException("The import contains secret values; the caller has no rights to secrets.");
        }

        // plan against the current state, deleted keys included (a write continues their history);
        // the plan also finds the errors that need the state, so one response lists every failing item
        var existingByKey = (await QueryEntitiesAsync(place, KeyValueState.All, keyPrefix: null, protections: null))
            .ToDictionary(entity => KeyValueRules.Normalize(entity.Key), StringComparer.Ordinal);
        var plan = new List<ImportPlanItem>();

        foreach (var item in document.Items.Where(item => !invalidKeys.Contains(item.Key)))
        {
            var normalizedKey = KeyValueRules.Normalize(item.Key);
            existingByKey.TryGetValue(normalizedKey, out var existing);
            var action = await PlanImportItemAsync(place, item, existing, errors);
            plan.Add(new ImportPlanItem(item, normalizedKey, existing, action));
        }

        if (errors.Count > 0)
        {
            throw new KeyValueImportInvalidException($"{errors.Count} item(s) of the import are not valid; nothing was written.", errors);
        }

        if (options.Mode == KeyValueImportMode.Replace)
        {
            var listed = plan.Select(item => item.NormalizedKey).ToHashSet(StringComparer.Ordinal);
            var deletes = existingByKey.Values
                .Where(entity => !entity.IsDeleted)
                .Where(entity => normalizedPrefix is null || KeyValueRules.Normalize(entity.Key).StartsWith(normalizedPrefix, StringComparison.Ordinal))
                .Where(entity => !listed.Contains(KeyValueRules.Normalize(entity.Key)))
                .OrderBy(entity => entity.Id, StringComparer.Ordinal)
                .Select(entity => new ImportPlanItem(
                    new KeyValueDocumentItem { Key = entity.Key, Protection = entity.Content.Protection },
                    KeyValueRules.Normalize(entity.Key),
                    entity,
                    KeyValueImportAction.Delete));
            plan.AddRange(deletes);
        }

        EnsureImportSecretRights(plan, caller);

        if (options.DryRun)
        {
            return new KeyValueImportResult
            {
                IsDryRun = true,
                IsComplete = true,
                Items = plan.Select(item => item.ToResult(item.Existing?.Version)).ToList(),
            };
        }

        var results = await ExecuteImportAsync(place, plan, caller);
        var written = results.Count(result => result.ErrorCode is null && result.Action is KeyValueImportAction.Create or KeyValueImportAction.Update or KeyValueImportAction.Delete);
        _usage?.Add(place.Organization, place.Project, KeyValueUsageMeters.Writes, written);
        await AuditAsync(
            place,
            caller,
            KeyValueAuditOperation.Import,
            key: null,
            version: null,
            protection: null,
            $"{options.Mode}: {results.Count(result => result.Action == KeyValueImportAction.Create)} created, "
            + $"{results.Count(result => result.Action == KeyValueImportAction.Update)} updated, "
            + $"{results.Count(result => result.Action == KeyValueImportAction.Delete)} deleted, "
            + $"{results.Count(result => result.ErrorCode is not null)} failed");

        return new KeyValueImportResult
        {
            IsDryRun = false,
            IsComplete = results.All(result => result.ErrorCode is null),
            Items = results,
        };
    }

    public async Task<KeyValueLookup?> LookupAsync(string organization, string project, string view, string key, bool includeSecrets)
    {
        if (!KeyValueRules.IsValidKey(key))
        {
            return null;
        }

        var place = GetPlace(organization, project, view);
        var (entity, _) = await ReadAsync(place, key);

        if (entity is null || entity.IsDeleted)
        {
            return null;
        }

        if (entity.Content.Protection.IsSecret() && !includeSecrets)
        {
            return null;
        }

        var value = await _protector.UnprotectAsync(place.Identity(entity), entity.Content);
        _usage?.Add(place.Organization, place.Project, KeyValueUsageMeters.ReadsBinding);

        if (entity.Content.Protection.IsSecret())
        {
            // the binding path has no caller parameter; the API set the ambient caller of the request
            _usage?.Add(place.Organization, place.Project, KeyValueUsageMeters.Reveals);
            _audit?.RecordBindingRead(place.Organization, place.Project, place.View, entity.Key, entity.Content.Protection, KeyValueCallerContext.Current ?? KeyValueCaller.System());
        }

        return new KeyValueLookup(entity.Key, entity.Content.Protection, value, entity.Content.ContentType);
    }

    internal static string GetId(string normalizedKey)
    {
        return $"{EntityIdPrefixTypes.KeyValue}.{normalizedKey}";
    }

    internal static string GetVersionIdPrefix(string normalizedKey)
    {
        return $"{EntityIdPrefixTypes.KeyValueVersion}.{normalizedKey}.";
    }

    internal static string GetVersionId(string normalizedKey, ulong version)
    {
        return $"{GetVersionIdPrefix(normalizedKey)}{version}";
    }

    private static void EnsureProtectionAllowed(KeyValuePolicy policy, KeyValueProtection protection)
    {
        if (!policy.AllowedProtections.Contains(protection))
        {
            throw KeyValueErrors.ProtectionNotAllowed(protection);
        }
    }

    // A reference is a public pair whose value names a secret elsewhere; the shape is checked, the secret never read.
    private static void EnsureReference(KeyValueProtection protection, string? value, string? contentType)
    {
        if (!KeyValueContentTypes.IsReference(contentType))
        {
            return;
        }

        if (protection != KeyValueProtection.Public)
        {
            throw new InvalidInputException("A secret reference is a public pair; the platform never resolves it.", ErrorCodes.InvalidSecretReference);
        }

        if (value is not null)
        {
            SecretReferences.Ensure(value, contentType!);
        }
    }

    private static void EnsureSecretWriteRights(KeyValueEntity existing, KeyValueProtection targetProtection, KeyValueCaller caller)
    {
        if ((existing.Content.Protection.IsSecret() || targetProtection.IsSecret()) && !caller.CanWriteSecrets)
        {
            throw KeyValueErrors.SecretRightsRequired(existing.Key);
        }
    }

    // null: no check; 0: the key must not exist or must be deleted; otherwise the current version.
    private static void EnsureExpectedVersion(KeyValueEntity existing, ulong? expectedVersion)
    {
        if (expectedVersion is null)
        {
            return;
        }

        var matches = expectedVersion == existing.Version || (expectedVersion == 0 && existing.IsDeleted);

        if (!matches)
        {
            throw KeyValueErrors.VersionMismatch(existing.Key, expectedVersion.Value, existing.IsDeleted ? 0 : existing.Version);
        }
    }

    private static Task BackOffAsync(int attempt)
    {
        return attempt == 0
            ? Task.CompletedTask
            : Task.Delay(Random.Shared.Next(10, 40 * attempt));
    }

    private static KeyValueBulkItemResult ToFailure(string key, Exception exception)
    {
        return new KeyValueBulkItemResult
        {
            Key = key,
            Outcome = exception is AccessDeniedException ? KeyValueBulkOutcome.Forbidden : KeyValueBulkOutcome.Failed,
            ErrorCode = (exception as StorytellerException)?.ErrorCode,
            Message = exception.Message,
        };
    }

    private Place GetPlace(string organization, string project, string view)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(organization);
        ArgumentException.ThrowIfNullOrWhiteSpace(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(view);

        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = PartitionKeys.GetKeyValues(project, view);
        return new Place(organization, project, view, repository.Container, partitionKeyValue, new PartitionKey(partitionKeyValue));
    }

    private QueryDefinition BuildListingQuery(KeyValueListRequest request)
    {
        var idPrefix = $"{EntityIdPrefixTypes.KeyValue}.";
        var text = "SELECT c.id, c.Key, c.Version, c.Author, c.UpdatedAt, c.DeletedAt, c.ttl, c._ts, "
            + "c.Content.Protection AS Protection, c.Content.ContentType AS ContentType";

        if (request.IncludeValues)
        {
            // never the value of a secret, whatever its form ("Value" is a keyword of the query language, hence the brackets)
            text += ", (c.Content.Protection = @public ? c.Content[\"Value\"] : null) AS PlainValue";
        }

        text += " FROM c WHERE STARTSWITH(c.id, @idPrefix)";

        switch (request.State)
        {
            case KeyValueState.Active:
                text += " AND NOT IS_DEFINED(c.DeletedAt)";
                break;

            case KeyValueState.Deleted:
                text += " AND IS_DEFINED(c.DeletedAt)";
                break;
        }

        if (!string.IsNullOrWhiteSpace(request.KeyPrefix))
        {
            text += " AND STARTSWITH(c.id, @keyPrefix)";
        }

        if (request.Protections is { Count: > 0 })
        {
            text += " AND ARRAY_CONTAINS(@protections, c.Content.Protection)";
        }

        text += " ORDER BY c.id";

        return new QueryDefinition(text)
            .WithParameter("@public", KeyValueProtection.Public.ToString())
            .WithParameter("@idPrefix", idPrefix)
            .WithParameter("@keyPrefix", $"{idPrefix}{KeyValueRules.Normalize(request.KeyPrefix?.Trim() ?? string.Empty)}")
            .WithParameter("@protections", (request.Protections ?? []).Select(protection => protection.ToString()).ToArray());
    }

    private async Task<(KeyValueEntity? Entity, string? ETag)> ReadAsync(Place place, string key)
    {
        return await place.Container.TryReadItemWithETagAsync(
            GetId(KeyValueRules.Normalize(key)),
            place.PartitionKey,
            stream => stream.DeserializeNewtonsoft<KeyValueEntity>(_serializerOptions));
    }

    private Task<KeyValueVersionEntity?> ReadVersionAsync(Place place, string normalizedKey, ulong version)
    {
        return place.Container.TryReadItemAsync(
            GetVersionId(normalizedKey, version),
            place.PartitionKey,
            stream => stream.DeserializeNewtonsoft<KeyValueVersionEntity>(_serializerOptions));
    }

    private async Task<string?> ResolveValueAsync(Place place, KeyValueEntity entity, bool reveal, KeyValueCaller caller)
    {
        return await ResolveValueAsync(place, entity.Key, entity.Version, entity.Content, reveal, caller);
    }

    private async Task<string?> ResolveValueAsync(Place place, string key, ulong version, StoredValue content, bool reveal, KeyValueCaller caller)
    {
        if (!content.Protection.IsSecret())
        {
            return content.Value;
        }

        if (!reveal)
        {
            return null;
        }

        if (!caller.CanReadSecrets)
        {
            throw KeyValueErrors.SecretRightsRequired(key);
        }

        return await _protector.UnprotectAsync(place.Identity(KeyValueRules.Normalize(key), version), content);
    }

    private async Task<bool> IsSameValueAsync(Place place, KeyValueEntity existing, string value)
    {
        if (!existing.Content.Protection.IsSecret())
        {
            return string.Equals(existing.Content.Value, value, StringComparison.Ordinal);
        }

        var current = await _protector.UnprotectAsync(place.Identity(existing), existing.Content);
        return string.Equals(current, value, StringComparison.Ordinal);
    }

    private async Task<string?> GetVersionTextAsync(Place place, string key, ulong version, KeyValueCaller caller)
    {
        if (version == 0)
        {
            return string.Empty;
        }

        var (catalog, _) = await ReadAsync(place, key);
        StoredValue? content = null;

        if (catalog is not null && catalog.Version == version)
        {
            content = catalog.Content;
        }
        else
        {
            content = (await ReadVersionAsync(place, KeyValueRules.Normalize(key), version))?.Content;
        }

        if (content is null)
        {
            return null;
        }

        if (content.Protection == KeyValueProtection.Hyok)
        {
            throw new InvalidInputException("A client-encrypted value cannot be compared on the server.", ErrorCodes.NotSupportedForProtection);
        }

        return await ResolveValueAsync(place, key, version, content, reveal: true, caller);
    }

    private async Task<ulong> GetNextVersionAsync(Place place, string normalizedKey)
    {
        var prefix = GetVersionIdPrefix(normalizedKey);
        var response = await place.Container.GetItemLinqQueryable<KeyValueVersionEntity>(
                requestOptions: new QueryRequestOptions { PartitionKey = place.PartitionKey })
            .Where(version => version.Id.StartsWith(prefix))
            .Select(version => version.Version)
            .MaxAsync();

        LogCharge("max-version", place, response.RequestCharge);
        return response.Resource + 1;
    }

    private int GetRemainingLifetime(DateTimeOffset creationTime, DateTimeOffset now)
    {
        var remaining = creationTime + _options.VersionRetention - now;
        return remaining > TimeSpan.Zero ? (int)Math.Ceiling(remaining.TotalSeconds) : 0;
    }

    // The history of the current version, unless the key is deleted (the delete wrote it) or nothing of its lifetime remains.
    private async Task AddHistoryOfCurrentAsync(Place place, TransactionalBatch batch, KeyValueEntity existing, DateTimeOffset now, bool checkExists)
    {
        if (existing.IsDeleted)
        {
            return;
        }

        var remaining = GetRemainingLifetime(existing.UpdatedAt, now);

        if (remaining <= 0)
        {
            return;
        }

        var historyId = GetVersionId(KeyValueRules.Normalize(existing.Key), existing.Version);

        if (checkExists && await place.Container.ExistsAsync(historyId, place.PartitionKey))
        {
            return;
        }

        batch.CreateItem(existing.ToVersion(remaining));
    }

    private async Task<KeyValueEntity?> TryWriteNextVersionAsync(Place place, KeyValueEntity existing, string etag, StoredValue content, string author, DateTimeOffset now)
    {
        var updated = existing with
        {
            Version = existing.Version + 1,
            Content = content,
            Author = author,
            UpdatedAt = now,
            DeletedAt = null,
            DeletedBy = null,
            TimeToLiveInSeconds = null,
        };

        var batch = place.Container.CreateTransactionalBatch(place.PartitionKey);
        await AddHistoryOfCurrentAsync(place, batch, existing, now, checkExists: false);
        batch.ReplaceItem(existing.Id, updated, new TransactionalBatchItemRequestOptions { IfMatchEtag = etag });

        return await TryExecuteWriteAsync(place, batch, "update", existing.Id) ? updated : null;
    }

    private async Task<bool> TrySoftDeleteAsync(Place place, KeyValueEntity existing, string etag, string author)
    {
        var now = _timeProvider.GetUtcNow();
        var deleted = existing with
        {
            DeletedAt = now,
            DeletedBy = author,
            TimeToLiveInSeconds = (int)Math.Max(1, _options.DeletedRetention.TotalSeconds),
        };

        var batch = place.Container.CreateTransactionalBatch(place.PartitionKey);
        await AddHistoryOfCurrentAsync(place, batch, existing, now, checkExists: true);
        batch.ReplaceItem(existing.Id, deleted, new TransactionalBatchItemRequestOptions { IfMatchEtag = etag });

        return await TryExecuteWriteAsync(place, batch, "delete", existing.Id);
    }

    private async Task<bool> TryRestoreAsync(Place place, KeyValueEntity existing, string etag)
    {
        var restored = existing with
        {
            DeletedAt = null,
            DeletedBy = null,
            TimeToLiveInSeconds = null,
        };

        var batch = place.Container.CreateTransactionalBatch(place.PartitionKey);
        batch.ReplaceItem(existing.Id, restored, new TransactionalBatchItemRequestOptions { IfMatchEtag = etag });

        // the delete wrote the history item of the current version; the current version has no history
        var historyId = GetVersionId(KeyValueRules.Normalize(existing.Key), existing.Version);

        if (await place.Container.ExistsAsync(historyId, place.PartitionKey))
        {
            batch.DeleteItem(historyId);
        }

        return await TryExecuteWriteAsync(place, batch, "restore", existing.Id);
    }

    // Every version first, the catalog item last: a failure leaves the key in place, and the same request finishes the job.
    private async Task PermanentlyDeleteAsync(Place place, KeyValueEntity existing, string etag)
    {
        var normalizedKey = KeyValueRules.Normalize(existing.Key);
        var ids = await QueryIdsAsync(place, GetVersionIdPrefix(normalizedKey));

        foreach (var chunk in ids.Chunk(MaxBatchOperations))
        {
            var batch = place.Container.CreateTransactionalBatch(place.PartitionKey);

            foreach (var id in chunk)
            {
                batch.DeleteItem(id);
            }

            using var response = await batch.ExecuteAsync();
            LogCharge("purge-versions", place, response.RequestCharge);

            if (!response.IsSuccessStatusCode)
            {
                throw new KeyValueStorageException(
                    $"Failed to delete the versions of '{existing.Id}' with status {response.StatusCode}: {response.ErrorMessage}",
                    response.StatusCode);
            }
        }

        try
        {
            var deleted = await place.Container.DeleteItemAsync<KeyValueEntity>(
                existing.Id,
                place.PartitionKey,
                new ItemRequestOptions { IfMatchEtag = etag });
            LogCharge("purge", place, deleted.RequestCharge);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw KeyValueErrors.Busy(place.View, existing.Key, 1);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            // already gone
        }
    }

    // True on success; false when another writer changed the key (409, 412, 404), which the caller retries.
    private async Task<bool> TryExecuteWriteAsync(Place place, TransactionalBatch batch, string operation, string id)
    {
        using var response = await batch.ExecuteAsync();
        LogCharge(operation, place, response.RequestCharge);

        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
        {
            return false;
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new KeyValueStorageException(
                $"Failed to write '{id}' with status {response.StatusCode}: {response.ErrorMessage}",
                response.StatusCode);
        }

        return true;
    }

    private async Task<IReadOnlyList<string>> QueryIdsAsync(Place place, string idPrefix)
    {
        var query = new QueryDefinition("SELECT VALUE c.id FROM c WHERE STARTSWITH(c.id, @prefix)")
            .WithParameter("@prefix", idPrefix);
        var ids = new List<string>();

        using var iterator = place.Container.GetItemQueryIterator<string>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = place.PartitionKey, MaxItemCount = CosmosConstants.MaxItemCountPerPage });

        while (iterator.HasMoreResults)
        {
            ids.AddRange(await iterator.ReadNextAsync());
        }

        return ids;
    }

    // Whole catalog items of a view, ordered by id.
    private async Task<IReadOnlyList<KeyValueEntity>> QueryEntitiesAsync(Place place, KeyValueState state, string? keyPrefix, IReadOnlyCollection<KeyValueProtection>? protections)
    {
        var idPrefix = $"{EntityIdPrefixTypes.KeyValue}.";
        var text = "SELECT * FROM c WHERE STARTSWITH(c.id, @idPrefix)";

        switch (state)
        {
            case KeyValueState.Active:
                text += " AND NOT IS_DEFINED(c.DeletedAt)";
                break;

            case KeyValueState.Deleted:
                text += " AND IS_DEFINED(c.DeletedAt)";
                break;
        }

        if (!string.IsNullOrWhiteSpace(keyPrefix))
        {
            text += " AND STARTSWITH(c.id, @keyPrefix)";
        }

        if (protections is { Count: > 0 })
        {
            text += " AND ARRAY_CONTAINS(@protections, c.Content.Protection)";
        }

        text += " ORDER BY c.id";

        var query = new QueryDefinition(text)
            .WithParameter("@idPrefix", idPrefix)
            .WithParameter("@keyPrefix", $"{idPrefix}{KeyValueRules.Normalize(keyPrefix?.Trim() ?? string.Empty)}")
            .WithParameter("@protections", (protections ?? []).Select(protection => protection.ToString()).ToArray());

        var entities = new List<KeyValueEntity>();
        using var iterator = place.Container.GetItemQueryStreamIterator(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = place.PartitionKey, MaxItemCount = CosmosConstants.MaxItemCountPerPage });

        while (iterator.HasMoreResults)
        {
            using var response = await iterator.ReadNextAsync();
            response.EnsureSuccessStatusCode();
            LogCharge("query", place, response.Headers.RequestCharge);
            var page = response.Content.DeserializeNewtonsoft<QueryPage<KeyValueEntity>>(_serializerOptions);
            entities.AddRange(page?.Documents ?? []);
        }

        return entities;
    }

    private async Task<KeyValueBulkResult> RunBulkAsync(
        string organization,
        string project,
        string view,
        KeyValueBulkRequest request,
        KeyValueCaller caller,
        KeyValueState prefixState,
        Func<Place, string, KeyValueCaller, Task<KeyValueBulkItemResult>> operation)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(caller);
        var place = GetPlace(organization, project, view);
        var keys = await ResolveBulkKeysAsync(place, request, prefixState);
        var results = new List<KeyValueBulkItemResult>(keys.Count);

        foreach (var key in keys)
        {
            try
            {
                results.Add(await operation(place, key, caller));
            }
            catch (Exception exception) when (exception is StorytellerException or KeyValueStorageException or KeyValueIntegrityException)
            {
                results.Add(ToFailure(key, exception));
            }
        }

        return new KeyValueBulkResult { Items = results };
    }

    private async Task<IReadOnlyList<string>> ResolveBulkKeysAsync(Place place, KeyValueBulkRequest request, KeyValueState prefixState)
    {
        if (request.Keys is { Count: > 0 })
        {
            if (request.Keys.Count > MaxBulkKeys)
            {
                throw new InvalidInputException($"A bulk operation takes at most {MaxBulkKeys} keys.", ErrorCodes.InvalidKey);
            }

            return request.Keys
                .Select(KeyValueRules.EnsureKey)
                .DistinctBy(KeyValueRules.Normalize, StringComparer.Ordinal)
                .ToList();
        }

        // an empty prefix selects every key of the view; only a missing one is an error
        if (request.KeyPrefix is null)
        {
            throw new InvalidInputException("Keys or a key prefix is required.", ErrorCodes.InvalidKey);
        }

        var keys = new List<string>();
        string? continuationToken = null;

        do
        {
            var page = await ListAsync(place.Organization, place.Project, place.View, new KeyValueListRequest
            {
                KeyPrefix = request.KeyPrefix,
                State = prefixState,
                ContinuationToken = continuationToken,
            });

            keys.AddRange(page.Items.Select(item => item.Key));
            continuationToken = page.ContinuationToken;

            if (keys.Count > MaxBulkPrefixKeys)
            {
                throw new InvalidInputException($"More than {MaxBulkPrefixKeys} keys match the prefix; narrow the prefix.", ErrorCodes.InvalidKey);
            }
        }
        while (!string.IsNullOrWhiteSpace(continuationToken));

        return keys;
    }

    private async Task<KeyValueBulkItemResult> RestoreOneAsync(Place place, string key, KeyValueCaller caller)
    {
        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            await BackOffAsync(attempt);
            var (existing, etag) = await ReadAsync(place, key);

            if (existing is null)
            {
                return new KeyValueBulkItemResult { Key = key, Outcome = KeyValueBulkOutcome.NotFound };
            }

            if (!existing.IsDeleted)
            {
                return new KeyValueBulkItemResult { Key = key, Outcome = KeyValueBulkOutcome.Unchanged, Version = existing.Version };
            }

            if (existing.Content.Protection.IsSecret() && !caller.CanWriteSecrets)
            {
                return new KeyValueBulkItemResult { Key = key, Outcome = KeyValueBulkOutcome.Forbidden, ErrorCode = ErrorCodes.AccessDenied };
            }

            if (await TryRestoreAsync(place, existing, etag!))
            {
                await RecordWriteAsync(place, caller, existing with { DeletedAt = null, DeletedBy = null }, KeyValueAuditOperation.Restore, previous: null);
                return new KeyValueBulkItemResult { Key = key, Outcome = KeyValueBulkOutcome.Done, Version = existing.Version };
            }
        }

        return ToFailure(key, KeyValueErrors.Busy(place.View, key, MaxRetries));
    }

    private async Task<KeyValueBulkItemResult> PurgeOneAsync(Place place, string key, KeyValueCaller caller)
    {
        var (existing, etag) = await ReadAsync(place, key);

        if (existing is null || !existing.IsDeleted)
        {
            return new KeyValueBulkItemResult { Key = key, Outcome = KeyValueBulkOutcome.NotFound };
        }

        if (existing.Content.Protection.IsSecret() && !caller.CanWriteSecrets)
        {
            return new KeyValueBulkItemResult { Key = key, Outcome = KeyValueBulkOutcome.Forbidden, ErrorCode = ErrorCodes.AccessDenied };
        }

        await PermanentlyDeleteAsync(place, existing, etag!);
        await RecordWriteAsync(place, caller, existing, KeyValueAuditOperation.Purge, previous: null);
        return new KeyValueBulkItemResult { Key = key, Outcome = KeyValueBulkOutcome.Done, Version = existing.Version };
    }

    private async Task<KeyValueBulkItemResult> ProtectOneAsync(Place place, string key, KeyValueProtection target, KeyValueCaller caller)
    {
        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            await BackOffAsync(attempt);
            var now = _timeProvider.GetUtcNow();
            var (existing, etag) = await ReadAsync(place, key);

            if (existing is null || existing.IsDeleted)
            {
                return new KeyValueBulkItemResult { Key = key, Outcome = KeyValueBulkOutcome.NotFound };
            }

            if (!target.IsServerSideEncrypted() || !existing.Content.Protection.IsServerSideEncrypted())
            {
                return new KeyValueBulkItemResult
                {
                    Key = key,
                    Outcome = KeyValueBulkOutcome.NotAllowed,
                    ErrorCode = ErrorCodes.ReprotectNotPossible,
                    Message = "Only Standard, Hsm and Byok can be changed on the server; Public and Hyok need a new value from the client.",
                };
            }

            if (existing.Content.Protection == target)
            {
                return new KeyValueBulkItemResult { Key = key, Outcome = KeyValueBulkOutcome.Unchanged, Version = existing.Version };
            }

            if (!caller.CanWriteSecrets)
            {
                return new KeyValueBulkItemResult { Key = key, Outcome = KeyValueBulkOutcome.Forbidden, ErrorCode = ErrorCodes.AccessDenied };
            }

            var plaintext = await _protector.UnprotectAsync(place.Identity(existing), existing.Content);
            var content = await _protector.ProtectAsync(place.Identity(KeyValueRules.Normalize(existing.Key), existing.Version + 1), target, plaintext, existing.Content.ContentType);
            var updated = await TryWriteNextVersionAsync(place, existing, etag!, content, caller.Author, now);

            if (updated is not null)
            {
                await RecordWriteAsync(place, caller, updated, KeyValueAuditOperation.Reprotect, existing.Content.Protection);
                return new KeyValueBulkItemResult { Key = key, Outcome = KeyValueBulkOutcome.Done, Version = updated.Version };
            }
        }

        return ToFailure(key, KeyValueErrors.Busy(place.View, key, MaxRetries));
    }

    private static List<KeyValueImportItemResult> ValidateImportItems(KeyValueDocument document, KeyValueImportOptions options, string? normalizedPrefix, KeyValuePolicy policy)
    {
        var errors = new List<KeyValueImportItemResult>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in document.Items)
        {
            try
            {
                KeyValueRules.EnsureKey(item.Key);
                KeyValueRules.EnsureContentType(item.ContentType);
                EnsureProtectionAllowed(policy, item.Protection);

                if (item.Value is not null)
                {
                    KeyValueRules.EnsureValue(item.Value, item.Protection);
                    EnsureReference(item.Protection, item.Value, item.ContentType);
                }
                else if (!item.Protection.IsSecret())
                {
                    throw new InvalidInputException($"The public item '{item.Key}' has no value.", ErrorCodes.ImportInvalid);
                }

                var normalizedKey = KeyValueRules.Normalize(item.Key);

                if (!seen.Add(normalizedKey))
                {
                    throw new InvalidInputException($"The key '{item.Key}' is listed more than once.", ErrorCodes.ImportInvalid);
                }

                if (options.Mode == KeyValueImportMode.Replace
                    && normalizedPrefix is not null
                    && !normalizedKey.StartsWith(normalizedPrefix, StringComparison.Ordinal))
                {
                    throw new InvalidInputException($"The key '{item.Key}' is outside the replaced prefix '{options.KeyPrefix}'.", ErrorCodes.ImportInvalid);
                }
            }
            catch (StorytellerException exception)
            {
                errors.Add(new KeyValueImportItemResult
                {
                    Key = item.Key,
                    Protection = item.Protection,
                    Action = KeyValueImportAction.Unchanged,
                    ErrorCode = exception.ErrorCode,
                    Message = exception.Message,
                });
            }
        }

        return errors;
    }

    private async Task<KeyValueImportAction> PlanImportItemAsync(Place place, KeyValueDocumentItem item, KeyValueEntity? existing, List<KeyValueImportItemResult> errors)
    {
        if (existing is null)
        {
            if (item.Value is null)
            {
                errors.Add(new KeyValueImportItemResult
                {
                    Key = item.Key,
                    Protection = item.Protection,
                    Action = KeyValueImportAction.Create,
                    ErrorCode = ErrorCodes.SecretValueMissing,
                    Message = $"The secret '{item.Key}' does not exist yet, so the import needs its value.",
                });
            }

            return KeyValueImportAction.Create;
        }

        if (item.Value is null)
        {
            // a secret exported without its value leaves the existing secret alone
            return KeyValueImportAction.Unchanged;
        }

        if (existing.IsDeleted)
        {
            return KeyValueImportAction.Create;
        }

        var same = item.Protection == existing.Content.Protection
            && string.Equals(item.ContentType, existing.Content.ContentType, StringComparison.Ordinal)
            && await IsSameValueAsync(place, existing, item.Value);

        return same ? KeyValueImportAction.Unchanged : KeyValueImportAction.Update;
    }

    private static void EnsureImportSecretRights(IReadOnlyList<ImportPlanItem> plan, KeyValueCaller caller)
    {
        if (caller.CanWriteSecrets)
        {
            return;
        }

        var touchesSecret = plan.Any(item =>
            item.Action is KeyValueImportAction.Create or KeyValueImportAction.Update or KeyValueImportAction.Delete
            && (item.Item.Protection.IsSecret() || (item.Existing?.Content.Protection.IsSecret() ?? false)));

        if (touchesSecret)
        {
            throw new AccessDeniedException("The import writes or deletes secrets; the caller has no rights to secrets.");
        }
    }

    private async Task<List<KeyValueImportItemResult>> ExecuteImportAsync(Place place, List<ImportPlanItem> plan, KeyValueCaller caller)
    {
        var results = new KeyValueImportItemResult[plan.Count];
        var now = _timeProvider.GetUtcNow();

        for (var index = 0; index < plan.Count; index++)
        {
            if (plan[index].Action is KeyValueImportAction.Unchanged)
            {
                results[index] = plan[index].ToResult(plan[index].Existing?.Version);
            }
        }

        // writes in atomic batches of 50 keys (history and catalog item), a conflicting batch falls back to single writes
        var writes = plan
            .Select((item, index) => (Item: item, Index: index))
            .Where(entry => entry.Item.Action is KeyValueImportAction.Create or KeyValueImportAction.Update)
            .ToList();

        foreach (var chunk in writes.Chunk(MaxImportBatchKeys))
        {
            var prepared = await PrepareImportWritesAsync(place, chunk.Select(entry => entry.Item).ToList(), caller, now);
            var batch = place.Container.CreateTransactionalBatch(place.PartitionKey);

            foreach (var (item, entity) in prepared)
            {
                if (item.Existing is null)
                {
                    batch.CreateItem(entity);
                }
                else
                {
                    await AddHistoryOfCurrentAsync(place, batch, item.Existing, now, checkExists: false);
                    batch.ReplaceItem(entity.Id, entity, new TransactionalBatchItemRequestOptions { IfMatchEtag = item.Existing.ETag });
                }
            }

            using var response = await batch.ExecuteAsync();
            LogCharge("import", place, response.RequestCharge);

            if (response.IsSuccessStatusCode)
            {
                foreach (var ((item, entity), entry) in prepared.Zip(chunk))
                {
                    results[entry.Index] = item.ToResult(entity.Version);
                }

                continue;
            }

            foreach (var entry in chunk)
            {
                results[entry.Index] = await ImportOneAsync(place, entry.Item, caller);
            }
        }

        foreach (var (item, index) in plan.Select((item, index) => (item, index)).Where(entry => entry.item.Action == KeyValueImportAction.Delete))
        {
            results[index] = await ImportDeleteAsync(place, item, caller);
        }

        return results.ToList();
    }

    // The highest stored version of each key, in one query; keys without versions are absent.
    private async Task<Dictionary<string, ulong>> GetMaxVersionsAsync(Place place, IReadOnlyCollection<string> normalizedKeys)
    {
        var versions = new Dictionary<string, ulong>(StringComparer.Ordinal);

        if (normalizedKeys.Count == 0)
        {
            return versions;
        }

        var query = new QueryDefinition(
                "SELECT c.NormalizedKey AS Key, MAX(c.Version) AS Version FROM c "
                + "WHERE STARTSWITH(c.id, @prefix) AND ARRAY_CONTAINS(@keys, c.NormalizedKey) GROUP BY c.NormalizedKey")
            .WithParameter("@prefix", $"{EntityIdPrefixTypes.KeyValueVersion}.")
            .WithParameter("@keys", normalizedKeys.ToArray());

        using var iterator = place.Container.GetItemQueryIterator<KeyValueMaxVersionRow>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = place.PartitionKey });

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync();
            LogCharge("max-versions", place, page.RequestCharge);

            foreach (var row in page)
            {
                versions[row.Key] = row.Version;
            }
        }

        return versions;
    }

    private async Task<List<(ImportPlanItem Item, KeyValueEntity Entity)>> PrepareImportWritesAsync(Place place, List<ImportPlanItem> items, KeyValueCaller caller, DateTimeOffset now)
    {
        var prepared = new (ImportPlanItem Item, KeyValueEntity Entity)[items.Count];
        var maxVersions = await GetMaxVersionsAsync(place, items.Where(item => item.Existing is null).Select(item => item.NormalizedKey).ToList());
        using var throttler = new SemaphoreSlim(MaxParallelProtections);

        await Task.WhenAll(items.Select(async (item, index) =>
        {
            await throttler.WaitAsync();

            try
            {
                var version = item.Existing?.Version + 1 ?? maxVersions.GetValueOrDefault(item.NormalizedKey) + 1;
                var content = await _protector.ProtectAsync(place.Identity(item.NormalizedKey, version), item.Item.Protection, item.Item.Value!, item.Item.ContentType);
                var entity = item.Existing is null
                    ? new KeyValueEntity
                    {
                        PartitionKey = place.PartitionKeyValue,
                        Id = GetId(item.NormalizedKey),
                        ProjectName = place.Project,
                        ViewName = place.View,
                        Key = item.Item.Key,
                        Version = version,
                        Content = content,
                        Author = caller.Author,
                        UpdatedAt = now,
                        CreatedBy = caller.Author,
                        CreatedAt = now,
                    }
                    : item.Existing with
                    {
                        Version = version,
                        Content = content,
                        Author = caller.Author,
                        UpdatedAt = now,
                        DeletedAt = null,
                        DeletedBy = null,
                        TimeToLiveInSeconds = null,
                    };

                prepared[index] = (item, entity);
            }
            finally
            {
                throttler.Release();
            }
        }));

        return prepared.ToList();
    }

    // The fallback of a conflicting batch: the single write re-reads the key and re-plans it.
    private async Task<KeyValueImportItemResult> ImportOneAsync(Place place, ImportPlanItem item, KeyValueCaller caller)
    {
        try
        {
            var (existing, _) = await ReadAsync(place, item.Item.Key);
            var written = await SetAsync(place.Organization, place.Project, place.View, item.Item.Key, new KeyValueWrite
            {
                Protection = item.Item.Protection,
                Value = item.Item.Value!,
                ContentType = item.Item.ContentType,
            }, caller);

            var action = existing is null || existing.IsDeleted
                ? KeyValueImportAction.Create
                : written.Version == existing.Version ? KeyValueImportAction.Unchanged : KeyValueImportAction.Update;

            return item.ToResult(written.Version) with { Action = action };
        }
        catch (Exception exception) when (exception is StorytellerException or KeyValueStorageException or KeyValueIntegrityException)
        {
            return item.ToResult(null) with
            {
                ErrorCode = (exception as StorytellerException)?.ErrorCode ?? ErrorCodes.Conflict,
                Message = exception.Message,
            };
        }
    }

    private async Task<KeyValueImportItemResult> ImportDeleteAsync(Place place, ImportPlanItem item, KeyValueCaller caller)
    {
        try
        {
            for (var attempt = 0; attempt < MaxRetries; attempt++)
            {
                await BackOffAsync(attempt);
                var (existing, etag) = await ReadAsync(place, item.Item.Key);

                if (existing is null || existing.IsDeleted)
                {
                    return item.ToResult(existing?.Version);
                }

                if (await TrySoftDeleteAsync(place, existing, etag!, caller.Author))
                {
                    return item.ToResult(existing.Version);
                }
            }

            throw KeyValueErrors.Busy(place.View, item.Item.Key, MaxRetries);
        }
        catch (Exception exception) when (exception is StorytellerException or KeyValueStorageException)
        {
            return item.ToResult(null) with
            {
                ErrorCode = (exception as StorytellerException)?.ErrorCode ?? ErrorCodes.Conflict,
                Message = exception.Message,
            };
        }
    }

    private void LogCharge(string operation, Place place, double requestCharge)
    {
        _logger.LogDebug("kv {Operation} in {PartitionKey}: {RequestCharge} RU", operation, place.PartitionKeyValue, requestCharge);
    }

    // Every write is metered; writes that touch a secret are audited.
    private async Task RecordWriteAsync(Place place, KeyValueCaller caller, KeyValueEntity entity, KeyValueAuditOperation operation, KeyValueProtection? previous)
    {
        _usage?.Add(place.Organization, place.Project, KeyValueUsageMeters.Writes);

        if (entity.Content.Protection.IsSecret() || (previous?.IsSecret() ?? false))
        {
            var detail = previous is not null && previous != entity.Content.Protection ? $"{previous} -> {entity.Content.Protection}" : null;
            await AuditAsync(place, caller, operation, entity.Key, entity.Version, entity.Content.Protection, detail);
        }
    }

    private async Task RecordRevealAsync(Place place, KeyValueCaller caller, string key, ulong version, KeyValueProtection protection, KeyValueAuditOperation operation)
    {
        _usage?.Add(place.Organization, place.Project, KeyValueUsageMeters.Reveals);
        await AuditAsync(place, caller, operation, key, version, protection, detail: null);
    }

    private async Task AuditAsync(Place place, KeyValueCaller caller, KeyValueAuditOperation operation, string? key, ulong? version, KeyValueProtection? protection, string? detail)
    {
        if (_audit is null)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        await _audit.WriteAsync(place.Organization, place.Project, new KeyValueAuditEvent
        {
            Id = CosmosKeyValueAuditLog.NewId(now),
            Time = now,
            View = place.View,
            Key = key,
            Version = version,
            Protection = protection,
            Operation = operation,
            Actor = caller.Author,
            Channel = caller.Channel,
            CorrelationId = caller.CorrelationId,
            Detail = detail,
        });
    }

    private sealed record Place(string Organization, string Project, string View, Container Container, string PartitionKeyValue, PartitionKey PartitionKey)
    {
        public KeyValueIdentity Identity(string normalizedKey, ulong version)
        {
            return new KeyValueIdentity(Organization, Project, View, normalizedKey, version);
        }

        public KeyValueIdentity Identity(KeyValueEntity entity)
        {
            return Identity(KeyValueRules.Normalize(entity.Key), entity.Version);
        }
    }

    private sealed record ImportPlanItem(KeyValueDocumentItem Item, string NormalizedKey, KeyValueEntity? Existing, KeyValueImportAction Action)
    {
        public KeyValueImportItemResult ToResult(ulong? version)
        {
            return new KeyValueImportItemResult
            {
                Key = Item.Key,
                Protection = Item.Protection,
                Action = Action,
                Version = version,
            };
        }
    }

    private sealed class QueryPage<T>
    {
        public List<T>? Documents { get; set; }
    }
}
