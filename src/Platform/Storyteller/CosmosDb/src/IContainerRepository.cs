using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller;

public interface IContainerRepository
{
    Container Container { get; }
}

public interface IContainerRepository<TContext> : IContainerRepository
    where TContext : IContainerContext
{
    // no members
}
