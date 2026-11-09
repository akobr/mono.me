using System.Collections.Concurrent;

namespace _42.Platform.Storyteller;

public class ContainerRepositoryProvider : IContainerRepositoryProvider
{
    private readonly ICosmosClientProvider _clientProvider;

    // Parallel requests of a cold worker ask for the same containers at once. Two entries at most: core and organizations.
    private readonly ConcurrentDictionary<string, IContainerRepository> _cache = new();

    public ContainerRepositoryProvider(ICosmosClientProvider clientProvider)
    {
        _clientProvider = clientProvider;
    }

    public IContainerRepository GetCore()
    {
        return GetContainer(CosmosConstants.CoreContainerName);
    }

    // A small scope per call over the one shared container, so nothing here grows with the number of organizations.
    public OrganizationContainer GetOrganizationContainer(string organizationName)
    {
        return new OrganizationContainer(organizationName, GetContainer(CosmosConstants.OrganizationsContainerName).Container);
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
