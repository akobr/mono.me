using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Entities.Access;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller;

// Machine certificates in Cosmos, so a certificate issued on one instance authenticates on every other one and
// survives a restart. Hits are cached for 5 minutes, misses for 30 seconds; a revocation is seen by other
// instances within the hit time.
public class CosmosClientCertificateStore : IClientCertificateStore
{
    private static readonly TimeSpan HitExpiration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MissExpiration = TimeSpan.FromSeconds(30);

    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly JsonSerializerSettings _serializerOptions;
    private readonly IMemoryCache _cache;
    private readonly TimeProvider _timeProvider;

    public CosmosClientCertificateStore(
        IContainerRepositoryProvider repositoryProvider,
        IOptions<JsonSerializerSettings> serializerOptions,
        IMemoryCache cache,
        TimeProvider? timeProvider = null)
    {
        _repositoryProvider = repositoryProvider;
        _serializerOptions = serializerOptions.Value;
        _cache = cache;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public static string GetPartitionKey(string project)
    {
        return $"{project}.access";
    }

    public async Task<CertificateRecord?> GetByMachineAccessIdAsync(string organization, string project, string machineAccessId)
    {
        var cacheKey = $"mcert:machine:{organization}.{project}:{machineAccessId}";

        if (_cache.TryGetValue(cacheKey, out CachedRecord? cached) && cached is not null)
        {
            return cached.Record;
        }

        // the newest record of the machine: a renewal writes a new one and keeps the previous
        var query = new QueryDefinition("SELECT * FROM c WHERE STARTSWITH(c.id, @prefix) AND c.MachineAccessId = @machine ORDER BY c.NotAfter DESC")
            .WithParameter("@prefix", MachineCertificateEntity.IdPrefix)
            .WithParameter("@machine", machineAccessId);

        var container = _repositoryProvider.GetOrganizationContainer(organization).Container;
        using var iterator = container.GetItemQueryIterator<MachineCertificateEntity>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(GetPartitionKey(project)), MaxItemCount = 1 });

        MachineCertificateEntity? newest = null;

        while (iterator.HasMoreResults && newest is null)
        {
            newest = (await iterator.ReadNextAsync()).FirstOrDefault();
        }

        var record = newest?.ToRecord();
        _cache.Set(cacheKey, new CachedRecord(record), record is null ? MissExpiration : HitExpiration);
        return record;
    }

    public async Task<CertificateRecord?> GetByThumbprintAsync(string organization, string project, string thumbprint)
    {
        var cacheKey = $"mcert:thumb:{organization}.{project}:{thumbprint.ToUpperInvariant()}";

        if (_cache.TryGetValue(cacheKey, out CachedRecord? cached) && cached is not null)
        {
            return cached.Record;
        }

        var container = _repositoryProvider.GetOrganizationContainer(organization).Container;
        var entity = await container.TryReadItemAsync(
            MachineCertificateEntity.GetId(thumbprint),
            new PartitionKey(GetPartitionKey(project)),
            stream => stream.DeserializeNewtonsoft<MachineCertificateEntity>(_serializerOptions));

        var record = entity?.ToRecord();
        _cache.Set(cacheKey, new CachedRecord(record), record is null ? MissExpiration : HitExpiration);
        return record;
    }

    public async Task StoreAsync(string organization, string project, CertificateRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var container = _repositoryProvider.GetOrganizationContainer(organization).Container;
        var partitionKeyValue = GetPartitionKey(project);
        var entity = MachineCertificateEntity.FromRecord(partitionKeyValue, record, _timeProvider.GetUtcNow());

        await container.UpsertItemAsync(entity, new PartitionKey(partitionKeyValue));
        Evict(organization, project, record.MachineAccessId, record.Thumbprint);
    }

    public async Task RevokeAsync(string organization, string project, string machineAccessId)
    {
        var container = _repositoryProvider.GetOrganizationContainer(organization).Container;
        var partitionKey = new PartitionKey(GetPartitionKey(project));
        var query = new QueryDefinition("SELECT * FROM c WHERE STARTSWITH(c.id, @prefix) AND c.MachineAccessId = @machine")
            .WithParameter("@prefix", MachineCertificateEntity.IdPrefix)
            .WithParameter("@machine", machineAccessId);

        using var iterator = container.GetItemQueryIterator<MachineCertificateEntity>(query, requestOptions: new QueryRequestOptions { PartitionKey = partitionKey });
        var entities = new List<MachineCertificateEntity>();

        while (iterator.HasMoreResults)
        {
            entities.AddRange(await iterator.ReadNextAsync());
        }

        foreach (var entity in entities.Where(entity => !entity.IsRevoked))
        {
            await container.ReplaceItemAsync(entity with { IsRevoked = true }, entity.Id, partitionKey);
        }

        foreach (var entity in entities)
        {
            Evict(organization, project, machineAccessId, entity.Thumbprint);
        }
    }

    private void Evict(string organization, string project, string machineAccessId, string thumbprint)
    {
        _cache.Remove($"mcert:machine:{organization}.{project}:{machineAccessId}");
        _cache.Remove($"mcert:thumb:{organization}.{project}:{thumbprint.ToUpperInvariant()}");
    }

    private sealed record CachedRecord(CertificateRecord? Record);
}
