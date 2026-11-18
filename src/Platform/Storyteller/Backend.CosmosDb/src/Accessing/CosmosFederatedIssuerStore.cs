using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Accessing.Federation;
using _42.Platform.Storyteller.Entities.Access;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

// Trusted issuers of a project: one document per issuer in the organization's data, partition {project}.federation
// (outside the machine list of {project}.access), id = name, next to the access audit records of their changes. The list is cached for a minute per instance and dropped
// on a local write, like the machine authentication policy; another instance sees a change within that minute.
public class CosmosFederatedIssuerStore : IFederatedIssuerStore
{
    private const string PartitionSuffix = "federation";
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromSeconds(60);

    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly IMemoryCache _cache;
    private readonly AccessAuditRecords _audit;

    public CosmosFederatedIssuerStore(
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

    public static string GetPartition(string project)
    {
        return $"{project}.{PartitionSuffix}";
    }

    public async Task<IReadOnlyList<FederatedIssuer>> ListAsync(string organization, string project, CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey(organization, project);

        if (_cache.TryGetValue(cacheKey, out IReadOnlyList<FederatedIssuer>? cached) && cached is not null)
        {
            return cached;
        }

        var entities = await QueryAsync(organization, project, cancellationToken);

        if (entities is null)
        {
            // No container organizations, so no project to cache issuers for.
            return [];
        }

        var issuers = entities
            .Select(ToModel)
            .OrderBy(issuer => issuer.Name, StringComparer.Ordinal)
            .ToList();
        _cache.Set<IReadOnlyList<FederatedIssuer>>(cacheKey, issuers, CacheExpiration);
        return issuers;
    }

    public async Task<FederatedIssuer?> GetAsync(string organization, string project, string name, CancellationToken cancellationToken = default)
    {
        if (!FederationValidation.IsValidName(name))
        {
            return null;
        }

        var entity = await ReadAsync(organization, project, name, cancellationToken);
        return entity is null ? null : ToModel(entity);
    }

    public Task<FederatedIssuer> SetAsync(string organization, string project, FederatedIssuer issuer, AccessCaller caller, CancellationToken cancellationToken = default)
    {
        return WriteAsync(organization, project, issuer, caller, createOnly: false, cancellationToken);
    }

    public Task<FederatedIssuer> CreateAsync(string organization, string project, FederatedIssuer issuer, AccessCaller caller, CancellationToken cancellationToken = default)
    {
        return WriteAsync(organization, project, issuer, caller, createOnly: true, cancellationToken);
    }

    // A create-only write never replaces: an issuer it finds, or one that appears before the batch creates it (409), is
    // a conflict.
    private async Task<FederatedIssuer> WriteAsync(string organization, string project, FederatedIssuer issuer, AccessCaller caller, bool createOnly, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(caller);

        if (!FederationValidation.IsValidName(issuer.Name))
        {
            throw new InvalidInputException($"The name '{issuer.Name}' of a trusted issuer is not valid.", ErrorCodes.InvalidFederation);
        }

        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = GetPartition(project);
        var partitionKey = repository.Key(partitionKeyValue);
        var existing = await ReadAsync(organization, project, issuer.Name, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        FederatedIssuerEntity entity;
        var batch = repository.Container.CreateTransactionalBatch(partitionKey);

        if (existing is null)
        {
            // Counted from the store, not the cached list, so concurrent instances cannot pass the limit together by much.
            if (await CountAsync(repository, partitionKeyValue, cancellationToken) >= FederationValidation.MaxIssuersPerProject)
            {
                throw new InvalidInputException(FederationValidation.TooManyIssuersMessage, ErrorCodes.InvalidFederation);
            }

            entity = ToEntity(issuer, repository.Organization, partitionKeyValue, now, now);
            batch.CreateItem(entity);
            batch.CreateItem(_audit.ForProject(repository.Organization, project, partitionKeyValue, new AccessAuditChange(AccessAuditAction.FederatedIssuerCreated, caller)
            {
                Target = AccessAuditTarget.Issuer(issuer.Name),
                NewValue = AccessAuditValues.Issuer(ToModel(entity)),
            }));
        }
        else if (createOnly)
        {
            throw new ConflictException($"The trusted issuer '{issuer.Name}' was created concurrently; reload and retry.");
        }
        else
        {
            // Rules reference issuers by name and were written for this Issuer.
            if (!string.Equals(existing.Issuer, issuer.Issuer, StringComparison.Ordinal))
            {
                throw new InvalidInputException(FederationValidation.IssuerImmutableMessage, ErrorCodes.InvalidFederation);
            }

            entity = ToEntity(issuer, repository.Organization, partitionKeyValue, existing.CreatedAt, now);
            batch.ReplaceItem(entity.Id, entity, new TransactionalBatchItemRequestOptions { IfMatchEtag = existing.ETag });
            var oldValue = AccessAuditValues.Issuer(ToModel(existing));
            var newValue = AccessAuditValues.Issuer(ToModel(entity));

            if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
            {
                batch.CreateItem(_audit.ForProject(repository.Organization, project, partitionKeyValue, new AccessAuditChange(AccessAuditAction.FederatedIssuerUpdated, caller)
                {
                    Target = AccessAuditTarget.Issuer(issuer.Name),
                    OldValue = oldValue,
                    NewValue = newValue,
                }));
            }
        }

        using var response = await batch.ExecuteAsync(cancellationToken);
        Invalidate(organization, project);

        if (response.Failed(HttpStatusCode.Conflict) || response.Failed(HttpStatusCode.PreconditionFailed))
        {
            throw new ConflictException($"The trusted issuer '{issuer.Name}' was changed concurrently; reload and retry.");
        }

        response.ThrowIfFailed($"Saving the trusted issuer '{issuer.Name}'");
        return ToModel(entity);
    }

    public async Task<bool> DeleteAsync(string organization, string project, string name, AccessCaller caller, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(caller);

        if (!FederationValidation.IsValidName(name))
        {
            return false;
        }

        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = GetPartition(project);

        // Deleted with the ETag of the read, so the record describes the version that was deleted; a change in between
        // is read again once.
        for (var attempt = 1; ; attempt++)
        {
            var existing = await ReadAsync(organization, project, name, cancellationToken);

            if (existing is null)
            {
                Invalidate(organization, project);
                return false;
            }

            using var response = await repository.Container.CreateTransactionalBatch(repository.Key(partitionKeyValue))
                .DeleteItem(name, new TransactionalBatchItemRequestOptions { IfMatchEtag = existing.ETag })
                .CreateItem(_audit.ForProject(repository.Organization, project, partitionKeyValue, new AccessAuditChange(AccessAuditAction.FederatedIssuerDeleted, caller)
                {
                    Target = AccessAuditTarget.Issuer(name),
                    OldValue = AccessAuditValues.Issuer(ToModel(existing)),
                }))
                .ExecuteAsync(cancellationToken);
            Invalidate(organization, project);

            if (response.Failed(HttpStatusCode.NotFound))
            {
                return false;
            }

            if (response.Failed(HttpStatusCode.PreconditionFailed))
            {
                if (attempt < 2)
                {
                    continue;
                }

                throw new ConflictException($"The trusted issuer '{name}' was changed concurrently; reload and retry.");
            }

            response.ThrowIfFailed($"Deleting the trusted issuer '{name}'");
            return true;
        }
    }

    private static string GetCacheKey(string organization, string project)
    {
        return $"fedissuers:{organization}.{project}";
    }

    private static async Task<int> CountAsync(OrganizationContainer repository, string partitionKey, CancellationToken cancellationToken)
    {
        using var iterator = repository.Container.GetItemQueryIterator<int>(
            new QueryDefinition("SELECT VALUE COUNT(1) FROM c WHERE NOT STARTSWITH(c.id, @audit)")
                .WithParameter("@audit", AccessAuditEntity.IdPrefix),
            requestOptions: repository.QueryOptions(partitionKey));
        var count = 0;

        while (iterator.HasMoreResults)
        {
            count += (await iterator.ReadNextAsync(cancellationToken)).Sum();
        }

        return count;
    }

    private static FederatedIssuer ToModel(FederatedIssuerEntity entity)
    {
        return new FederatedIssuer
        {
            Name = entity.Id,
            Issuer = entity.Issuer,
            KeySource = entity.KeySource,
            JwksUri = entity.JwksUri,
            Jwks = entity.Jwks,
            MaxTokenLifetimeSeconds = entity.MaxTokenLifetimeSeconds,
            RequiredClaims = entity.RequiredClaims?.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value,
                StringComparer.Ordinal),
            Description = entity.Description,
        };
    }

    private static FederatedIssuerEntity ToEntity(FederatedIssuer issuer, string organization, string partitionKey, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        return new FederatedIssuerEntity
        {
            PartitionKey = partitionKey,
            Organization = organization,
            Id = issuer.Name,
            Issuer = issuer.Issuer,
            KeySource = issuer.KeySource,
            JwksUri = issuer.JwksUri,
            Jwks = issuer.Jwks,
            MaxTokenLifetimeSeconds = issuer.MaxTokenLifetimeSeconds,
            RequiredClaims = issuer.RequiredClaims?.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToList(),
                StringComparer.Ordinal),
            Description = issuer.Description,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt,
        };
    }

    private void Invalidate(string organization, string project)
    {
        _cache.Remove(GetCacheKey(organization, project));
    }

    private async Task<FederatedIssuerEntity?> ReadAsync(string organization, string project, string name, CancellationToken cancellationToken)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        return await repository.Container.TryReadItemAsync(
            name,
            repository.Key(GetPartition(project)),
            stream => stream.DeserializeSystemTextJson<FederatedIssuerEntity>(_serializerOptions),
            cancellationToken: cancellationToken);
    }

    // Null when the container organizations doesn't exist; a missing organization is only an empty partition.
    private async Task<List<FederatedIssuerEntity>?> QueryAsync(string organization, string project, CancellationToken cancellationToken)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var entities = new List<FederatedIssuerEntity>();

        using var iterator = repository.Container.GetItemQueryStreamIterator(
            new QueryDefinition("SELECT * FROM c WHERE NOT STARTSWITH(c.id, @audit)")
                .WithParameter("@audit", AccessAuditEntity.IdPrefix),
            requestOptions: repository.QueryOptions(GetPartition(project)));

        while (iterator.HasMoreResults)
        {
            using var response = await iterator.ReadNextAsync(cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            var page = await JsonSerializer.DeserializeAsync<QueryPage<FederatedIssuerEntity>>(response.Content, _serializerOptions, cancellationToken);
            entities.AddRange(page?.Documents ?? []);
        }

        return entities;
    }

    private sealed record QueryPage<T>(List<T>? Documents);
}
