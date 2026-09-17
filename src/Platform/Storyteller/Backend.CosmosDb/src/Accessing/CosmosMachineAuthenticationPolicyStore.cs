using System;
using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Entities.Access;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

public class CosmosMachineAuthenticationPolicyStore : IMachineAuthenticationPolicyStore
{
    private const string Partition = "access";
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromSeconds(60);

    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly IMemoryCache _cache;

    public CosmosMachineAuthenticationPolicyStore(
        IContainerRepositoryProvider repositoryProvider,
        IOptions<JsonSerializerOptions> serializerOptions,
        IMemoryCache cache)
    {
        _repositoryProvider = repositoryProvider;
        _serializerOptions = serializerOptions.Value;
        _cache = cache;
    }

    public async Task<MachineAuthenticationPolicy?> GetAsync(string organization, string project)
    {
        var cacheKey = $"machauth:{organization}.{project}";

        if (_cache.TryGetValue(cacheKey, out MachineAuthenticationPolicy? cached))
        {
            return cached;
        }

        var repository = _repositoryProvider.GetCore();
        var accessPointId = $"apt.{organization}.{project}";
        var entity = await repository.Container.TryReadItemAsync(
            accessPointId,
            new PartitionKey(Partition),
            stream => stream.DeserializeSystemTextJson<AccessPointEntity>(_serializerOptions));

        var policy = entity?.MachineAuthentication;

        _cache.Set(cacheKey, policy, CacheExpiration);
        return policy;
    }

    public async Task SetAsync(string organization, string project, MachineAuthenticationPolicy policy)
    {
        var repository = _repositoryProvider.GetCore();
        var accessPointId = $"apt.{organization}.{project}";
        var partitionKey = new PartitionKey(Partition);

        var entity = await repository.Container.TryReadItemAsync(
            accessPointId,
            partitionKey,
            stream => stream.DeserializeSystemTextJson<AccessPointEntity>(_serializerOptions));

        if (entity is null)
        {
            throw new InvalidOperationException($"The access point '{organization}.{project}' doesn't exist.");
        }

        var updated = entity with { MachineAuthentication = policy };
        await repository.Container.UpsertItemAsync(updated, partitionKey);

        var cacheKey = $"machauth:{organization}.{project}";
        _cache.Set(cacheKey, policy, CacheExpiration);
    }
}
