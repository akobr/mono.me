using System;
using System.Collections.Concurrent;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Hands out the organization scopes of the shared provider over the shared container wrapped in an InterceptedContainer.
// A test builds its own service over it, so the shared singletons stay as they are.
public sealed class InterceptedRepositoryProvider(IContainerRepositoryProvider inner) : IContainerRepositoryProvider
{
    private readonly ConcurrentDictionary<string, OrganizationContainer> _organizations = new(StringComparer.Ordinal);

    public StorageInterceptor Interceptor { get; } = new();

    public IContainerRepository GetCore()
    {
        return inner.GetCore();
    }

    public OrganizationContainer GetOrganizationContainer(string organizationName)
    {
        return _organizations.GetOrAdd(
            organizationName,
            name => new OrganizationContainer(name, new InterceptedContainer(inner.GetOrganizationContainer(name).Container, Interceptor)));
    }
}
