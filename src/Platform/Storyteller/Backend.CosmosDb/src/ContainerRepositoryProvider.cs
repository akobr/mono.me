using System.Collections.Generic;

namespace _42.Platform.Storyteller;

public class ContainerRepositoryProvider : IContainerRepositoryProvider
{
    private readonly ICosmosClientProvider _clientProvider;
    private readonly Dictionary<string, IContainerRepository> _cache = new();

    public ContainerRepositoryProvider(ICosmosClientProvider clientProvider)
    {
        _clientProvider = clientProvider;
    }

    public IContainerRepository GetCore()
    {
        return GetContainer(CosmosConstants.CoreContainerName);
    }

    public IContainerRepository GetOrganizationContainer(string organizationName)
    {
        return GetContainer($"{CosmosConstants.OrganizationContainerNamePrefix}{organizationName}");
    }

    private IContainerRepository GetContainer(string containerName)
    {
        if (_cache.TryGetValue(containerName, out var repository))
        {
            return repository;
        }

        var container = _clientProvider.Client.GetContainer(CosmosConstants.DatabaseName, containerName);
        _cache[containerName] = repository = new ContainerRepository(container);
        return repository;
    }
}
