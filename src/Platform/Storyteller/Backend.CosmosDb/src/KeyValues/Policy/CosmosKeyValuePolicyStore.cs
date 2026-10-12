using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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

    public CosmosKeyValuePolicyStore(
        IContainerRepositoryProvider repositoryProvider,
        IOptions<JsonSerializerOptions> serializerOptions,
        IMemoryCache cache)
    {
        _repositoryProvider = repositoryProvider;
        _serializerOptions = serializerOptions.Value;
        _cache = cache;
    }

    public async Task<KeyValuePolicy> GetAsync(string organization, string project, CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey(organization, project);

        if (_cache.TryGetValue(cacheKey, out KeyValuePolicy? cached) && cached is not null)
        {
            return cached;
        }

        var (entity, _) = await ReadAsync(organization, project, cancellationToken);
        var policy = entity?.KeyValues ?? KeyValuePolicyDefaults.Create();
        _cache.Set(cacheKey, policy, CacheExpiration);
        return policy;
    }

    public async Task<KeyValuePolicy> SetAsync(string organization, string project, KeyValuePolicy policy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var (entity, etag) = await ReadAsync(organization, project, cancellationToken);

        if (entity is null)
        {
            throw new NotFoundException($"The project '{organization}.{project}' does not exist.");
        }

        var storedVersion = entity.KeyValues?.Version ?? 0;

        if (policy.Version != storedVersion + 1)
        {
            throw new ConflictException(
                $"The key/value policy of '{organization}.{project}' is at version {storedVersion}; reload and retry.",
                ErrorCodes.PolicyVersionMismatch);
        }

        var repository = _repositoryProvider.GetCore();

        try
        {
            await repository.Container.ReplaceItemAsync(
                entity with { KeyValues = policy },
                entity.Id,
                new PartitionKey(Partition),
                new ItemRequestOptions { IfMatchEtag = etag },
                cancellationToken);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw new ConflictException(
                $"The access point '{organization}.{project}' was changed concurrently; reload and retry.",
                ErrorCodes.PolicyVersionMismatch);
        }

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
