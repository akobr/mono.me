using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Entities.Access;
using _42.Platform.Storyteller.KeyValues.Policy;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.KeyValues;

// The policy lives on the project's access point (core container, partition "access"), next to the machine
// authentication policy. Reads are cached for a minute; writes are conditional on the policy version.
public class CosmosKeyValuePolicyStore : IKeyValuePolicyStore
{
    private const string Partition = "access";
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromSeconds(60);

    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly IMemoryCache _cache;
    private readonly AccessAuditRecords _audit;

    public CosmosKeyValuePolicyStore(
        IContainerRepositoryProvider repositoryProvider,
        IOptions<JsonSerializerOptions> serializerOptions,
        IMemoryCache cache,
        AccessAuditRecords? audit = null)
    {
        _repositoryProvider = repositoryProvider;
        _serializerOptions = serializerOptions.Value;
        _cache = cache;
        _audit = audit ?? AccessAuditRecords.Default;
    }

    public async Task<KeyValuePolicy> GetAsync(string organization, string project, CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey(organization, project);

        if (_cache.TryGetValue(cacheKey, out KeyValuePolicy? cached) && cached is not null)
        {
            return cached;
        }

        return await GetCurrentAsync(organization, project, cancellationToken);
    }

    public async Task<KeyValuePolicy> GetCurrentAsync(string organization, string project, CancellationToken cancellationToken = default)
    {
        var (entity, _) = await ReadAsync(organization, project, cancellationToken);
        var policy = entity?.KeyValues ?? KeyValuePolicyDefaults.Create();

        // a slow read can put back a copy older than a concurrent save's; the cache expires within a minute, and every
        // change reads past it, so a stale copy never rejects a current version for longer
        _cache.Set(GetCacheKey(organization, project), policy, CacheExpiration);
        return policy;
    }

    public async Task<KeyValuePolicy> SetAsync(string organization, string project, KeyValuePolicy policy, AccessCaller caller, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(caller);
        var (entity, etag) = await ReadAsync(organization, project, cancellationToken);

        if (entity is null)
        {
            throw new NotFoundException($"The project '{organization}.{project}' does not exist.");
        }

        var storedVersion = entity.KeyValues?.Version ?? 0;

        if (policy.Version != storedVersion + 1)
        {
            _cache.Remove(GetCacheKey(organization, project));
            throw new ConflictException(
                $"The key/value policy of '{organization}.{project}' is at version {storedVersion}; reload and retry.",
                ErrorCodes.PolicyVersionMismatch);
        }

        var repository = _repositoryProvider.GetCore();
        var batch = repository.Container.CreateTransactionalBatch(new PartitionKey(Partition))
            .ReplaceItem(entity.Id, entity with { KeyValues = policy }, new TransactionalBatchItemRequestOptions { IfMatchEtag = etag });

        // Only what decides who gets secrets and how is recorded: key versions, health and rewrap progress change no view.
        var oldView = AccessAuditValues.KeyValuePolicy(entity.KeyValues ?? KeyValuePolicyDefaults.Create());
        var newView = AccessAuditValues.KeyValuePolicy(policy);

        if (!string.Equals(oldView, newView, StringComparison.Ordinal))
        {
            batch.CreateItem(_audit.ForAccessPoint($"{organization}.{project}", new AccessAuditChange(AccessAuditAction.KeyValuePolicyChanged, caller)
            {
                OldValue = oldView,
                NewValue = newView,
            }));
        }

        using var response = await batch.ExecuteAsync(cancellationToken);

        if (response.Failed(HttpStatusCode.PreconditionFailed))
        {
            _cache.Remove(GetCacheKey(organization, project));
            throw new ConflictException(
                $"The access point '{organization}.{project}' was changed concurrently; reload and retry.",
                ErrorCodes.PolicyVersionMismatch);
        }

        response.ThrowIfFailed($"Saving the key/value policy of '{organization}.{project}'");

        _cache.Set(GetCacheKey(organization, project), policy, CacheExpiration);
        return policy;
    }

    public async Task<IReadOnlyList<(string Organization, string Project, KeyValuePolicy Policy)>> ListAsync(CancellationToken cancellationToken = default)
    {
        var repository = _repositoryProvider.GetCore();
        var query = new QueryDefinition("SELECT * FROM c WHERE STARTSWITH(c.id, 'apt.') AND IS_DEFINED(c.KeyValues)");
        var result = new List<(string, string, KeyValuePolicy)>();

        using var iterator = repository.Container.GetItemQueryStreamIterator(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(Partition) });

        while (iterator.HasMoreResults)
        {
            using var response = await iterator.ReadNextAsync(cancellationToken);
            response.EnsureSuccessStatusCode();
            var page = await JsonSerializer.DeserializeAsync<QueryPage<AccessPointEntity>>(response.Content, _serializerOptions, cancellationToken);

            foreach (var entity in page?.Documents ?? [])
            {
                var segments = entity.Key.Split('.', 2);

                if (segments.Length == 2 && entity.KeyValues is not null)
                {
                    result.Add((segments[0], segments[1], entity.KeyValues));
                }
            }
        }

        return result;
    }

    private static string GetCacheKey(string organization, string project)
    {
        return $"kvpolicy:{organization}.{project}";
    }

    private async Task<(AccessPointEntity? Entity, string? ETag)> ReadAsync(string organization, string project, CancellationToken cancellationToken)
    {
        var repository = _repositoryProvider.GetCore();
        return await repository.Container.TryReadItemWithETagAsync(
            $"apt.{organization}.{project}",
            new PartitionKey(Partition),
            stream => stream.DeserializeSystemTextJson<AccessPointEntity>(_serializerOptions),
            cancellationToken: cancellationToken);
    }

    private sealed record QueryPage<T>(List<T>? Documents);
}
