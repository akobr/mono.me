using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities.Access;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller.Accessing;

// The organizations of the platform. Their data shares the container "organizations", so no container names them:
// core is the record of which organizations exist, by their access points in the partition "access".
public sealed class CosmosOrganizationCatalog
{
    private readonly IContainerRepositoryProvider _repositoryProvider;

    public CosmosOrganizationCatalog(IContainerRepositoryProvider repositoryProvider)
    {
        _repositoryProvider = repositoryProvider;
    }

    // The names of the organizations in ordinal order: the access points whose key has no dot ("org.project" is a project).
    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        var repository = _repositoryProvider.GetCore();
        var query = new QueryDefinition("SELECT VALUE c.Key FROM c WHERE STARTSWITH(c.id, @prefix) AND NOT CONTAINS(c.Key, @separator)")
            .WithParameter("@prefix", AccessPointEntity.IdPrefix)
            .WithParameter("@separator", ".");
        var organizations = new List<string>();

        using var iterator = repository.Container.GetItemQueryIterator<string>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(AccessPointEntity.Partition), MaxItemCount = CosmosConstants.MaxItemCountPerPage });

        while (iterator.HasMoreResults)
        {
            organizations.AddRange(await iterator.ReadNextAsync(cancellationToken));
        }

        organizations.Sort(StringComparer.Ordinal);
        return organizations;
    }
}
