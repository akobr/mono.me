using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller;

public class ContainerRepository : IContainerRepository
{
    public ContainerRepository(Container container)
    {
        Container = container;
    }

    public Container Container { get; }
}
