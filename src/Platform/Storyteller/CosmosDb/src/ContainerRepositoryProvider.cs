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
        return Get("core");
    }

    public IContainerRepository Get(string containerName)
    {
        if (_cache.TryGetValue(containerName, out var repository))
        {
            return repository;
        }

        var container = _clientProvider.Client.GetContainer("42.Platform.2S", containerName);
        _cache[containerName] = repository = new ContainerRepository(container);
        return repository;
    }
}
