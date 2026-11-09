#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using Xunit;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The request charges of the shared container stay those of one container per organization: a point read with the full
// key is one request unit, and a query over one organization, with the predicate and the key prefix, costs about as much as
// a query in one partition. Only an emulator like the service charges request units (the vNext emulator answers 1 RU).
[Trait(CosmosTestEmulator.TraitName, CosmosTestEmulator.ClassicTrait)]
public class RequestChargeTests(Startup startup) : BaseTestsClass(startup)
{
    private const string Project = "billing";

    private readonly string _organization = $"ru-{Guid.NewGuid():N}"[..20];
    private readonly string _neighbour = $"ru-{Guid.NewGuid():N}"[..20];

    [Fact]
    public async Task PointRead_WithTheFullKey_CostsOneRequestUnit()
    {
        Assert.SkipUnless(Context.Emulator.IsLikeTheService, CosmosTestEmulator.SkipReasonOnVNext);
        var repository = await SeedAsync();

        var response = await repository.Container.ReadItemAsync<JObject>("kv.key-0", repository.Key($"{Project}.kv.default"), cancellationToken: TestContext.Current.CancellationToken);

        response.RequestCharge.Should().BeLessThanOrEqualTo(1.5);
    }

    [Fact]
    public async Task OrganizationQuery_WithThePrefix_CostsLikeAPartitionQuery()
    {
        Assert.SkipUnless(Context.Emulator.IsLikeTheService, CosmosTestEmulator.SkipReasonOnVNext);
        var repository = await SeedAsync();

        var partition = await ChargeAsync(
            repository,
            new QueryDefinition("SELECT * FROM c WHERE STARTSWITH(c.id, @prefix)").WithParameter("@prefix", "kv."),
            repository.QueryOptions($"{Project}.kv.default"));
        var organization = await ChargeAsync(
            repository,
            repository.WithOrganization(new QueryDefinition("SELECT * FROM c WHERE c.Organization = @organization AND STARTSWITH(c.PartitionKey, @project)")
                .WithParameter("@project", $"{Project}.")),
            repository.QueryOptions());

        partition.Items.Should().Be(3);
        organization.Items.Should().Be(6, "both views of the organization, none of the neighbour's");
        partition.Charge.Should().BeLessThan(10);
        organization.Charge.Should().BeLessThan(partition.Charge * 3, "the prefix keeps the query in the organization's partitions");
    }

    // Three small items in each of two views of the organization, and the same items in a neighbour organization.
    private async Task<OrganizationContainer> SeedAsync()
    {
        var provider = Context.Services.GetRequiredService<IContainerRepositoryProvider>();

        foreach (var organization in new[] { _organization, _neighbour })
        {
            var repository = provider.GetOrganizationContainer(organization);

            foreach (var view in new[] { "default", "prod" })
            {
                for (var index = 0; index < 3; index++)
                {
                    var partition = $"{Project}.kv.{view}";
                    var item = new JObject
                    {
                        ["id"] = $"kv.key-{index}",
                        ["Organization"] = organization,
                        ["PartitionKey"] = partition,
                        ["Value"] = $"{organization}-{view}-{index}",
                    };

                    await repository.Container.UpsertItemAsync(item, repository.Key(partition), cancellationToken: TestContext.Current.CancellationToken);
                }
            }
        }

        return provider.GetOrganizationContainer(_organization);
    }

    private static async Task<(int Items, double Charge)> ChargeAsync(OrganizationContainer repository, QueryDefinition query, QueryRequestOptions options)
    {
        var items = new List<JObject>();
        double charge = 0;
        using var iterator = repository.Container.GetItemQueryIterator<JObject>(query, requestOptions: options);

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(TestContext.Current.CancellationToken);
            charge += page.RequestCharge;
            items.AddRange(page);
        }

        items.Should().OnlyContain(item => item.Value<string>("Organization") == repository.Organization);
        return (items.Count, charge);
    }
}
