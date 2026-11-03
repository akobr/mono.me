#nullable enable

using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Moq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The repositories of the containers, without Cosmos DB (no emulator starts, none is wiped). The race of the former
// Dictionary cannot be forced, so this is a smoke test of the concurrent cache.
public class ContainerRepositoryProviderTests
{
    [Fact]
    public async Task ParallelCalls_GetOneRepositoryPerContainer()
    {
        var client = new Mock<CosmosClient>();
        client
            .Setup(cosmos => cosmos.GetContainer(CosmosConstants.DatabaseName, It.IsAny<string>()))
            .Returns((string _, string name) => Mock.Of<Container>(container => container.Id == name));
        var clientProvider = Mock.Of<ICosmosClientProvider>(provider => provider.Client == client.Object);
        var provider = new ContainerRepositoryProvider(clientProvider);
        string[] names = ["northwind", "contoso", "fabrikam"];

        var repositories = await Task.WhenAll(Enumerable.Range(0, 300).Select(index => Task.Run(
            () => (Name: names[index % names.Length], Repository: provider.GetOrganizationContainer(names[index % names.Length])),
            TestContext.Current.CancellationToken)));

        foreach (var group in repositories.GroupBy(pair => pair.Name))
        {
            group.Select(pair => pair.Repository).Distinct().Should().ContainSingle();
            group.First().Repository.Container.Id.Should().Be($"{CosmosConstants.OrganizationContainerNamePrefix}{group.Key}");
        }

        provider.GetCore().Container.Id.Should().Be(CosmosConstants.CoreContainerName);
    }
}
