using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.DbCreator.IntegrationTests;

// What a target holds in the database, with the ETag of every item: the items of the organization in the container
// organizations ("{PartitionKey}/{id}") and, in core, its access points and the owner's account ("core/{id}"). Two
// equal snapshots mean that nothing was written in between.
public static class StoredItems
{
    // the partition of accounts and access points in core (CosmosAccessService)
    private const string AccessPartition = "access";

    public static async Task<IReadOnlyDictionary<string, string>> ReadAsync(
        IServiceProvider services,
        string organization,
        string project,
        string ownerId)
    {
        var provider = services.GetRequiredService<IContainerRepositoryProvider>();
        var items = await ReadOrganizationAsync(services, organization);
        var core = provider.GetCore().Container;

        foreach (var id in new[] { $"apt.{organization}", $"apt.{organization}.{project}", ownerId })
        {
            using var response = await core.ReadItemStreamAsync(id, new PartitionKey(AccessPartition));

            if (response.IsSuccessStatusCode)
            {
                items[$"core/{id}"] = response.Headers.ETag;
            }
        }

        return items;
    }

    // The items of the organization in the container organizations, by "{PartitionKey}/{id}".
    public static async Task<Dictionary<string, string>> ReadOrganizationAsync(IServiceProvider services, string organization)
    {
        var repository = services.GetRequiredService<IContainerRepositoryProvider>().GetOrganizationContainer(organization);
        var query = repository.WithOrganization(
            new QueryDefinition("SELECT c.id, c.PartitionKey, c._etag FROM c WHERE c.Organization = @organization"));
        var items = new Dictionary<string, string>(StringComparer.Ordinal);
        using var iterator = repository.Container.GetItemQueryStreamIterator(query, requestOptions: repository.QueryOptions());

        while (iterator.HasMoreResults)
        {
            using var response = await iterator.ReadNextAsync();
            response.EnsureSuccessStatusCode();
            using var page = await JsonDocument.ParseAsync(response.Content);

            foreach (var item in page.RootElement.GetProperty("Documents").EnumerateArray())
            {
                var key = $"{item.GetProperty("PartitionKey").GetString()}/{item.GetProperty("id").GetString()}";
                items[key] = item.GetProperty("_etag").GetString()!;
            }
        }

        return items;
    }
}
