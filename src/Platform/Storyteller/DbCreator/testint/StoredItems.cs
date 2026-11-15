using System.Text.Json;
using _42.Platform.Storyteller.Entities.Access;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;

namespace _42.Platform.Storyteller.DbCreator.IntegrationTests;

// What a target holds in the database, with the ETag of every item: the items of the organization in the container
// organizations ("{PartitionKey}/{id}") and, in core, its access points, the owner's account and the organization's
// access audit records ("core/{id}"). Two equal snapshots mean that nothing was written in between, no record of an
// access change either.
public static class StoredItems
{
    // the partition of accounts, access points and their access audit records in core (CosmosAccessService)
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

            // only a missing item is absent; any other failure fails the test
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                continue;
            }

            response.EnsureSuccessStatusCode();
            items[$"core/{id}"] = response.Headers.ETag;
        }

        using var records = core.GetItemQueryStreamIterator(
            AccessAuditQuery("SELECT c.id, c._etag", organization),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(AccessPartition) });
        await CollectAsync(records, items, item => $"core/{item.GetProperty("id").GetString()}");

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
        await CollectAsync(iterator, items, item => $"{item.GetProperty("PartitionKey").GetString()}/{item.GetProperty("id").GetString()}");

        return items;
    }

    // The access audit records of the organization in core "access" (its access points and memberships), oldest first:
    // the id order is the time order.
    public static async Task<IReadOnlyList<AccessAuditEntity>> ReadAccessAuditAsync(IServiceProvider services, string organization)
    {
        var core = services.GetRequiredService<IContainerRepositoryProvider>().GetCore().Container;
        var records = new List<AccessAuditEntity>();
        using var iterator = core.GetItemQueryIterator<AccessAuditEntity>(
            AccessAuditQuery("SELECT *", organization),
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(AccessPartition) });

        while (iterator.HasMoreResults)
        {
            records.AddRange(await iterator.ReadNextAsync());
        }

        return records.OrderBy(record => record.Id, StringComparer.Ordinal).ToList();
    }

    private static QueryDefinition AccessAuditQuery(string select, string organization)
    {
        return new QueryDefinition($"{select} FROM c WHERE STARTSWITH(c.id, @prefix) AND c.Organization = @organization")
            .WithParameter("@prefix", AccessAuditEntity.IdPrefix)
            .WithParameter("@organization", organization);
    }

    // The ETag of every item of the pages, by the key of the item.
    private static async Task CollectAsync(FeedIterator iterator, Dictionary<string, string> items, Func<JsonElement, string> keyOf)
    {
        while (iterator.HasMoreResults)
        {
            using var response = await iterator.ReadNextAsync();
            response.EnsureSuccessStatusCode();
            using var page = await JsonDocument.ParseAsync(response.Content);

            foreach (var item in page.RootElement.GetProperty("Documents").EnumerateArray())
            {
                items[keyOf(item)] = item.GetProperty("_etag").GetString()!;
            }
        }
    }
}
