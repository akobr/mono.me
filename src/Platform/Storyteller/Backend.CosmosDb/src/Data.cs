using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities.Access;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller;

public class Data
{
    // Grants the account the given roles and adds it to each access point with the same role, keeping what is already
    // stored: other memberships of the account, other members and settings of the access points. Missing access points
    // are created; an organization needs nothing more, its data lives in the shared container organizations.
    public static async Task SeedAccountAccessAsync(
        Container container,
        string accountId,
        string accountUserName,
        string accountName,
        IReadOnlyDictionary<string, AccountRole> roles)
    {
        await MergeAsync<AccountEntity>(
            container,
            accountId,
            account =>
            {
                var accessMap = new Dictionary<string, AccountRole>(account?.AccessMap ?? []);
                foreach (var (key, role) in roles)
                {
                    accessMap[key] = role;
                }

                return account is null
                    ? new AccountEntity { Id = accountId, UserName = accountUserName, Name = accountName, AccessMap = accessMap }
                    : account with { AccessMap = accessMap };
            });

        foreach (var (key, role) in roles)
        {
            await MergeAsync<AccessPointEntity>(
                container,
                $"apt.{key}",
                accessPoint =>
                {
                    var accessMap = new Dictionary<string, AccountRole>(accessPoint?.AccessMap ?? []) { [accountId] = role };
                    return accessPoint is null
                        ? new AccessPointEntity { Key = key, AccessMap = accessMap }
                        : accessPoint with { AccessMap = accessMap };
                });
        }
    }

    // Read, change and write back with optimistic concurrency: a replace only with the ETag that was read, a create only
    // when nothing is stored. When the API changed the document in between, it is read again and the change reapplied.
    private static async Task MergeAsync<T>(Container container, string id, Func<T?, T> apply)
        where T : class
    {
        var partitionKey = new PartitionKey("access");
        for (var attempt = 1; ; attempt++)
        {
            T? current = null;
            string? etag = null;
            try
            {
                var response = await container.ReadItemAsync<T>(id, partitionKey);
                (current, etag) = (response.Resource, response.ETag);
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
            }

            try
            {
                var updated = apply(current);
                if (etag is null)
                {
                    await container.CreateItemAsync(updated, partitionKey);
                }
                else
                {
                    await container.ReplaceItemAsync(updated, id, partitionKey, new ItemRequestOptions { IfMatchEtag = etag });
                }

                return;
            }
            catch (CosmosException exception) when (attempt < 5
                && exception.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.Conflict)
            {
            }
        }
    }
}
