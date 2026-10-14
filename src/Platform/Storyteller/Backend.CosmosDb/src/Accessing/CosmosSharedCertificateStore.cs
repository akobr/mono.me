using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Model;
using _42.Platform.Storyteller.Entities.Access;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

// Shared certificates of a project. A call that presents one reads it by thumbprint: hits are cached for 5 minutes,
// misses for 30 seconds, like machine certificates. The admin list is cached per project, 5 minutes when it has
// certificates and 30 seconds when it is empty. A revocation is seen by other instances within 5 minutes.
public class CosmosSharedCertificateStore : ISharedCertificateStore
{
    private const string IdPrefix = "scr.";

    private static readonly TimeSpan ListExpiration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan EmptyListExpiration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HitExpiration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MissExpiration = TimeSpan.FromSeconds(30);

    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly IMemoryCache _cache;

    public CosmosSharedCertificateStore(
        IContainerRepositoryProvider repositoryProvider,
        IOptions<JsonSerializerOptions> serializerOptions,
        IMemoryCache cache)
    {
        _repositoryProvider = repositoryProvider;
        _serializerOptions = serializerOptions.Value;
        _cache = cache;
    }

    public async Task<IReadOnlyList<SharedCertificate>> ListAsync(string organization, string project)
    {
        var cacheKey = GetCacheKey(organization, project);

        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<SharedCertificate>? cached) && cached is not null)
        {
            return cached;
        }

        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = new PartitionKey($"{project}.access");

        var query = new QueryDefinition("SELECT * FROM c WHERE STARTSWITH(c.id, @prefix)")
            .WithParameter("@prefix", IdPrefix);

        using var iterator = repository.Container.GetItemQueryIterator<SharedCertificateEntity>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = partitionKey });

        var results = new List<SharedCertificate>();

        while (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();

            foreach (var entity in response)
            {
                results.Add(ToModel(entity));
            }
        }

        _cache.Set<IReadOnlyList<SharedCertificate>>(cacheKey, results, results.Count == 0 ? EmptyListExpiration : ListExpiration);
        return results;
    }

    public async Task<SharedCertificate?> GetByThumbprintAsync(string organization, string project, string thumbprint)
    {
        var cacheKey = GetThumbprintCacheKey(organization, project, thumbprint);

        if (_cache.TryGetValue(cacheKey, out CachedCertificate? cached) && cached is not null)
        {
            return cached.Certificate;
        }

        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var entity = await repository.Container.TryReadItemAsync(
            $"{IdPrefix}{thumbprint.ToUpperInvariant()}",
            new PartitionKey($"{project}.access"),
            stream => stream.DeserializeSystemTextJson<SharedCertificateEntity>(_serializerOptions));

        var certificate = entity is null ? null : ToModel(entity);
        _cache.Set(cacheKey, new CachedCertificate(certificate), certificate is null ? MissExpiration : HitExpiration);
        return certificate;
    }

    public async Task StoreAsync(string organization, string project, SharedCertificate certificate)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = $"{project}.access";
        var partitionKey = new PartitionKey(partitionKeyValue);

        var entity = new SharedCertificateEntity
        {
            PartitionKey = partitionKeyValue,
            Id = $"{IdPrefix}{certificate.Thumbprint}",
            Label = certificate.Label,
            Thumbprint = certificate.Thumbprint,
            NotBefore = certificate.NotBefore,
            NotAfter = certificate.NotAfter,
            IsRevoked = false,
        };

        await repository.Container.CreateItemAsync(entity, partitionKey);
        Evict(organization, project, certificate.Thumbprint);
    }

    public async Task<bool> RevokeAsync(string organization, string project, string thumbprint)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = $"{project}.access";
        var partitionKey = new PartitionKey(partitionKeyValue);
        var id = $"{IdPrefix}{thumbprint}";

        var entity = await repository.Container.TryReadItemAsync(
            id,
            partitionKey,
            stream => stream.DeserializeSystemTextJson<SharedCertificateEntity>(_serializerOptions));

        if (entity is null)
        {
            Evict(organization, project, thumbprint);
            return false;
        }

        var updated = entity with { IsRevoked = true };
        await repository.Container.ReplaceItemAsync(updated, id, partitionKey);
        Evict(organization, project, thumbprint);
        return true;
    }

    public async Task<bool> LabelExistsAsync(string organization, string project, string label)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKey = new PartitionKey($"{project}.access");

        var query = new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE STARTSWITH(c.id, @prefix) AND c.Label = @label")
            .WithParameter("@prefix", IdPrefix)
            .WithParameter("@label", label);

        using var iterator = repository.Container.GetItemQueryIterator<int>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = partitionKey });

        if (iterator.HasMoreResults)
        {
            var response = await iterator.ReadNextAsync();
            return response.FirstOrDefault() > 0;
        }

        return false;
    }

    private static string GetCacheKey(string organization, string project)
    {
        return $"scert:list:{organization}.{project}";
    }

    private static string GetThumbprintCacheKey(string organization, string project, string thumbprint)
    {
        return $"scert:thumb:{organization}.{project}:{thumbprint.ToUpperInvariant()}";
    }

    private static SharedCertificate ToModel(SharedCertificateEntity entity) => new()
    {
        Thumbprint = entity.Thumbprint,
        Label = entity.Label,
        NotBefore = entity.NotBefore,
        NotAfter = entity.NotAfter,
        IsRevoked = entity.IsRevoked,
    };

    private void Evict(string organization, string project, string thumbprint)
    {
        _cache.Remove(GetCacheKey(organization, project));
        _cache.Remove(GetThumbprintCacheKey(organization, project, thumbprint));
    }

    private sealed record CachedCertificate(SharedCertificate? Certificate);
}
