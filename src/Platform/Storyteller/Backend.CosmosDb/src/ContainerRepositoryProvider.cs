using System.Collections.Concurrent;

namespace _42.Platform.Storyteller;

public class ContainerRepositoryProvider : IContainerRepositoryProvider
{
    private readonly ICosmosClientProvider _clientProvider;

    // Parallel requests of a cold worker ask for the same containers at once.
    private readonly ConcurrentDictionary<string, IContainerRepository> _cache = new();

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
        // A hit does not touch the client (its lazy creation runs once, on the first miss).
        if (_cache.TryGetValue(containerName, out var repository))
        {
            return repository;
        }

        return _cache.GetOrAdd(
            containerName,
            static (name, client) => new ContainerRepository(client.GetContainer(CosmosConstants.DatabaseName, name)),
            _clientProvider.Client);
    }
}
