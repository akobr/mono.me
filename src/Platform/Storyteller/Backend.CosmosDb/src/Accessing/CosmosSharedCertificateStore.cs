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
    internal const string IdPrefix = "scr.";

    private static readonly TimeSpan ListExpiration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan EmptyListExpiration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan HitExpiration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MissExpiration = TimeSpan.FromSeconds(30);

    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly IMemoryCache _cache;
    private readonly AccessAuditRecords _audit;

    public CosmosSharedCertificateStore(
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

    public async Task<IReadOnlyList<SharedCertificate>> ListAsync(string organization, string project)
    {
        var cacheKey = GetCacheKey(organization, project);

        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<SharedCertificate>? cached) && cached is not null)
        {
            return cached;
        }

        var repository = _repositoryProvider.GetOrganizationContainer(organization);

        var query = new QueryDefinition("SELECT * FROM c WHERE STARTSWITH(c.id, @prefix)")
            .WithParameter("@prefix", IdPrefix);

        using var iterator = repository.Container.GetItemQueryIterator<SharedCertificateEntity>(
            query,
            requestOptions: repository.QueryOptions($"{project}.access"));

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
            repository.Key($"{project}.access"),
            stream => stream.DeserializeSystemTextJson<SharedCertificateEntity>(_serializerOptions));

        var certificate = entity is null ? null : ToModel(entity);
        _cache.Set(cacheKey, new CachedCertificate(certificate), certificate is null ? MissExpiration : HitExpiration);
        return certificate;
    }

    public async Task StoreAsync(string organization, string project, SharedCertificate certificate, AccessCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = $"{project}.access";
        var partitionKey = repository.Key(partitionKeyValue);

        var entity = new SharedCertificateEntity
        {
            PartitionKey = partitionKeyValue,
            Organization = repository.Organization,
            Id = $"{IdPrefix}{certificate.Thumbprint}",
            Label = certificate.Label,
            Thumbprint = certificate.Thumbprint,
            NotBefore = certificate.NotBefore,
            NotAfter = certificate.NotAfter,
            IsRevoked = false,
        };

        // The certificate and its record in one batch; never the PFX or its password, which are not stored either.
        using var response = await repository.Container.CreateTransactionalBatch(partitionKey)
            .CreateItem(entity)
            .CreateItem(_audit.ForProject(repository.Organization, project, partitionKeyValue, new AccessAuditChange(AccessAuditAction.SharedCertificateIssued, caller)
            {
                Target = AccessAuditTarget.Certificate(certificate.Thumbprint, certificate.Label),
                NewValue = AccessAuditValues.Certificate(ToModel(entity)),
            }))
            .ExecuteAsync();
        response.ThrowIfFailed($"Storing the shared certificate {certificate.Thumbprint}");
        Evict(organization, project, certificate.Thumbprint);
    }

    public async Task<bool> RevokeAsync(string organization, string project, string thumbprint, AccessCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = $"{project}.access";
        var partitionKey = repository.Key(partitionKeyValue);
        var id = $"{IdPrefix}{thumbprint}";

        // Replaced with the ETag of the read, so of two concurrent revocations only one commits and records; the other
        // reads the certificate again and finds it revoked.
        for (var attempt = 1; ; attempt++)
        {
            var (entity, eTag) = await repository.Container.TryReadItemWithETagAsync(
                id,
                partitionKey,
                stream => stream.DeserializeSystemTextJson<SharedCertificateEntity>(_serializerOptions));

            if (entity is null)
            {
                Evict(organization, project, thumbprint);
                return false;
            }

            if (entity.IsRevoked)
            {
                Evict(organization, project, thumbprint);
                return true;
            }

            var updated = entity with { IsRevoked = true };
            using var response = await repository.Container.CreateTransactionalBatch(partitionKey)
                .ReplaceItem(id, updated, new TransactionalBatchItemRequestOptions { IfMatchEtag = eTag })
                .CreateItem(_audit.ForProject(repository.Organization, project, partitionKeyValue, new AccessAuditChange(AccessAuditAction.SharedCertificateRevoked, caller)
                {
                    Target = AccessAuditTarget.Certificate(entity.Thumbprint, entity.Label),
                    OldValue = AccessAuditValues.Certificate(ToModel(entity)),
                    NewValue = AccessAuditValues.Certificate(ToModel(updated)),
                }))
                .ExecuteAsync();
            Evict(organization, project, thumbprint);

            if (response.Failed(HttpStatusCode.NotFound))
            {
                return false;
            }

            if (response.Failed(HttpStatusCode.PreconditionFailed) && attempt < 3)
            {
                continue;
            }

            response.ThrowIfFailed($"Revoking the shared certificate {thumbprint}");
            return true;
        }
    }

    public async Task<bool> LabelExistsAsync(string organization, string project, string label)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);

        var query = new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE STARTSWITH(c.id, @prefix) AND c.Label = @label")
            .WithParameter("@prefix", IdPrefix)
            .WithParameter("@label", label);

        using var iterator = repository.Container.GetItemQueryIterator<int>(
            query,
            requestOptions: repository.QueryOptions($"{project}.access"));

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
