using System;
using System.Collections.Concurrent;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// Hands out the organization scopes of the shared provider over the shared container wrapped in an InterceptedContainer,
// and the container core wrapped in one of its own (CoreInterceptor: the accounts, the access points and their access
// audit records). A test builds its own service over it, so the shared singletons stay as they are.
public sealed class InterceptedRepositoryProvider(IContainerRepositoryProvider inner) : IContainerRepositoryProvider
{
    private readonly ConcurrentDictionary<string, OrganizationContainer> _organizations = new(StringComparer.Ordinal);
    private IContainerRepository _core;

    public StorageInterceptor Interceptor { get; } = new();

    public StorageInterceptor CoreInterceptor { get; } = new();

    public IContainerRepository GetCore()
    {
        return _core ??= new CoreRepository(new InterceptedContainer(inner.GetCore().Container, CoreInterceptor));
    }

    public OrganizationContainer GetOrganizationContainer(string organizationName)
    {
        return _organizations.GetOrAdd(
            organizationName,
            name => new OrganizationContainer(name, new InterceptedContainer(inner.GetOrganizationContainer(name).Container, Interceptor)));
    }

    private sealed class CoreRepository(Container container) : IContainerRepository
    {
        public Container Container => container;
    }
}
