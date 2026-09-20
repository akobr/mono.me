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
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller;

public class CosmosSharedCertificateStore : ISharedCertificateStore
{
    private const string IdPrefix = "scr.";

    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly JsonSerializerOptions _serializerOptions;

    public CosmosSharedCertificateStore(
        IContainerRepositoryProvider repositoryProvider,
        IOptions<JsonSerializerOptions> serializerOptions)
    {
        _repositoryProvider = repositoryProvider;
        _serializerOptions = serializerOptions.Value;
    }

    public async Task<IReadOnlyList<SharedCertificate>> ListAsync(string organization, string project)
    {
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
                results.Add(new SharedCertificate
                {
                    Thumbprint = entity.Thumbprint,
                    Label = entity.Label,
                    NotBefore = entity.NotBefore,
                    NotAfter = entity.NotAfter,
                    IsRevoked = entity.IsRevoked,
                });
            }
        }

        return results;
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
            return false;
        }

        var updated = entity with { IsRevoked = true };
        await repository.Container.ReplaceItemAsync(updated, id, partitionKey);
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
}
