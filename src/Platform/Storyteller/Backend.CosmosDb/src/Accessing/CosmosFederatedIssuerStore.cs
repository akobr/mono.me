using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing.Federation;
using _42.Platform.Storyteller.Entities.Access;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

// Trusted issuers of a project: one document per issuer in the organization container, partition {project}.federation
// (outside the machine list of {project}.access), id = name. The list is cached for a minute per instance and dropped
// on a local write, like the machine authentication policy; another instance sees a change within that minute.
public class CosmosFederatedIssuerStore : IFederatedIssuerStore
{
    private const string PartitionSuffix = "federation";
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromSeconds(60);

    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly IMemoryCache _cache;

    public CosmosFederatedIssuerStore(
        IContainerRepositoryProvider repositoryProvider,
        IOptions<JsonSerializerOptions> serializerOptions,
        IMemoryCache cache)
    {
        _repositoryProvider = repositoryProvider;
        _serializerOptions = serializerOptions.Value;
        _cache = cache;
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
            // No organization container, so no project to cache issuers for.
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

    public async Task<FederatedIssuer> SetAsync(string organization, string project, FederatedIssuer issuer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(issuer);

        if (!FederationValidation.IsValidName(issuer.Name))
        {
            throw new InvalidInputException($"The name '{issuer.Name}' of a trusted issuer is not valid.", ErrorCodes.InvalidFederation);
        }

        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var partitionKeyValue = GetPartition(project);
        var partitionKey = new PartitionKey(partitionKeyValue);
        var existing = await ReadAsync(organization, project, issuer.Name, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        FederatedIssuerEntity entity;

        try
        {
            if (existing is null)
            {
                // Counted from the store, not the cached list, so concurrent instances cannot pass the limit together by much.
                if (await CountAsync(repository.Container, partitionKey, cancellationToken) >= FederationValidation.MaxIssuersPerProject)
                {
                    throw new InvalidInputException(FederationValidation.TooManyIssuersMessage, ErrorCodes.InvalidFederation);
                }

                entity = ToEntity(issuer, partitionKeyValue, now, now);
                await repository.Container.CreateItemAsync(entity, partitionKey, cancellationToken: cancellationToken);
            }
            else
            {
                // Rules reference issuers by name and were written for this Issuer.
                if (!string.Equals(existing.Issuer, issuer.Issuer, StringComparison.Ordinal))
                {
                    throw new InvalidInputException(FederationValidation.IssuerImmutableMessage, ErrorCodes.InvalidFederation);
                }

                entity = ToEntity(issuer, partitionKeyValue, existing.CreatedAt, now);
                await repository.Container.ReplaceItemAsync(
                    entity,
                    entity.Id,
                    partitionKey,
                    new ItemRequestOptions { IfMatchEtag = existing.ETag },
                    cancellationToken);
            }
        }
        catch (CosmosException exception) when (exception.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
        {
            Invalidate(organization, project);
            throw new ConflictException($"The trusted issuer '{issuer.Name}' was changed concurrently; reload and retry.");
        }

        Invalidate(organization, project);
        return ToModel(entity);
    }

    public async Task<bool> DeleteAsync(string organization, string project, string name, CancellationToken cancellationToken = default)
    {
        if (!FederationValidation.IsValidName(name))
        {
            return false;
        }

        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        using var response = await repository.Container.DeleteItemStreamAsync(
            name,
            new PartitionKey(GetPartition(project)),
            cancellationToken: cancellationToken);
        Invalidate(organization, project);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        return true;
    }

    private static string GetCacheKey(string organization, string project)
    {
        return $"fedissuers:{organization}.{project}";
    }

    private static async Task<int> CountAsync(Container container, PartitionKey partitionKey, CancellationToken cancellationToken)
    {
        using var iterator = container.GetItemQueryIterator<int>(
            new QueryDefinition("SELECT VALUE COUNT(1) FROM c"),
            requestOptions: new QueryRequestOptions { PartitionKey = partitionKey });
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

    private static FederatedIssuerEntity ToEntity(FederatedIssuer issuer, string partitionKey, DateTimeOffset createdAt, DateTimeOffset updatedAt)
    {
        return new FederatedIssuerEntity
        {
            PartitionKey = partitionKey,
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
            new PartitionKey(GetPartition(project)),
            stream => stream.DeserializeSystemTextJson<FederatedIssuerEntity>(_serializerOptions),
            cancellationToken: cancellationToken);
    }

    // Null when the organization container doesn't exist.
    private async Task<List<FederatedIssuerEntity>?> QueryAsync(string organization, string project, CancellationToken cancellationToken)
    {
        var repository = _repositoryProvider.GetOrganizationContainer(organization);
        var entities = new List<FederatedIssuerEntity>();

        using var iterator = repository.Container.GetItemQueryStreamIterator(
            new QueryDefinition("SELECT * FROM c"),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(GetPartition(project)) });

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
