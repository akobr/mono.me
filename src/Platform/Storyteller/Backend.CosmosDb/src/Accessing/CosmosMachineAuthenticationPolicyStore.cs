using System;
using System.Net;
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
    private readonly AccessAuditRecords _audit;

    public CosmosMachineAuthenticationPolicyStore(
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

    public async Task SetAsync(string organization, string project, MachineAuthenticationPolicy policy, AccessCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var repository = _repositoryProvider.GetCore();
        var accessPointId = $"apt.{organization}.{project}";
        var partitionKey = new PartitionKey(Partition);

        // Replaced with its ETag, so a membership, the settings or a key/value policy saved meanwhile is never overwritten; such a
        // write only moves the ETag, so one retry suffices.
        for (var attempt = 1; ; attempt++)
        {
            var (entity, eTag) = await repository.Container.TryReadItemWithETagAsync(
                accessPointId,
                partitionKey,
                stream => stream.DeserializeSystemTextJson<AccessPointEntity>(_serializerOptions));

            if (entity is null)
            {
                throw new InvalidOperationException($"The access point '{organization}.{project}' doesn't exist.");
            }

            var updated = entity with { MachineAuthentication = policy };
            var batch = repository.Container.CreateTransactionalBatch(partitionKey)
                .ReplaceItem(accessPointId, updated, new TransactionalBatchItemRequestOptions { IfMatchEtag = eTag });
            var oldValue = AccessAuditValues.MachineAuthentication(entity.MachineAuthentication);
            var newValue = AccessAuditValues.MachineAuthentication(policy);

            if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
            {
                batch.CreateItem(_audit.ForAccessPoint($"{organization}.{project}", new AccessAuditChange(AccessAuditAction.MachineAuthenticationPolicyChanged, caller)
                {
                    OldValue = oldValue,
                    NewValue = newValue,
                }));
            }

            using var response = await batch.ExecuteAsync();

            if (response.IsSuccessStatusCode)
            {
                break;
            }

            if (!response.Failed(HttpStatusCode.PreconditionFailed))
            {
                response.ThrowIfFailed($"Saving the machine authentication policy of '{organization}.{project}'");
            }

            if (attempt > 1)
            {
                throw new ConflictException($"The project '{organization}.{project}' was changed concurrently, try again.", ErrorCodes.Conflict);
            }
        }

        var cacheKey = $"machauth:{organization}.{project}";
        _cache.Set(cacheKey, policy, CacheExpiration);
    }
}
