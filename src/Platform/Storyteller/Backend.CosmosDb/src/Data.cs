using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Entities.Access;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller;

public class Data
{
    // Grants the account the given roles and adds it to each access point with the same role, keeping what is already
    // stored: other memberships of the account, other members and settings of the access points. Missing access points
    // are created; an organization needs nothing more, its data lives in the shared container organizations. Every
    // access point it creates or changes gets its access audit record by System in the same batch.
    public static async Task SeedAccountAccessAsync(
        Container container,
        string accountId,
        string accountUserName,
        string accountName,
        IReadOnlyDictionary<string, AccountRole> roles,
        AccessAuditRecords? audit = null)
    {
        audit ??= AccessAuditRecords.Default;

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
            await MergeAccessPointAsync(container, key, accountId, accountName, role, audit);
        }
    }

    // The membership of the account in one access point with its record, read and written with optimistic concurrency
    // like MergeAsync. An unchanged role writes and records nothing.
    private static async Task MergeAccessPointAsync(
        Container container,
        string key,
        string accountId,
        string accountName,
        AccountRole role,
        AccessAuditRecords audit)
    {
        var partitionKey = new PartitionKey(AccessPointEntity.Partition);
        var id = $"{AccessPointEntity.IdPrefix}{key}";

        for (var attempt = 1; ; attempt++)
        {
            AccessPointEntity? current = null;
            string? etag = null;
            try
            {
                var read = await container.ReadItemAsync<AccessPointEntity>(id, partitionKey);
                (current, etag) = (read.Resource, read.ETag);
            }
            catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
            {
            }

            AccountRole? oldRole = current is not null && current.AccessMap.TryGetValue(accountId, out var stored) ? stored : null;

            if (current is not null && oldRole == role)
            {
                return;
            }

            var accessMap = new Dictionary<string, AccountRole>(current?.AccessMap ?? []) { [accountId] = role };
            var updated = current is null
                ? new AccessPointEntity { Key = key, AccessMap = accessMap }
                : current with { AccessMap = accessMap };
            var action = current is null
                ? AccessAuditAction.AccessPointCreated
                : oldRole is null ? AccessAuditAction.MemberGranted : AccessAuditAction.MemberRoleChanged;
            var batch = container.CreateTransactionalBatch(partitionKey);

            if (etag is null)
            {
                batch.CreateItem(updated);
            }
            else
            {
                batch.ReplaceItem(id, updated, new TransactionalBatchItemRequestOptions { IfMatchEtag = etag });
            }

            batch.CreateItem(audit.ForAccessPoint(key, new AccessAuditChange(action, AccessCaller.System)
            {
                Target = AccessAuditTarget.Account(accountId, accountName),
                OldValue = AccessAuditValues.Role(oldRole),
                NewValue = AccessAuditValues.Role(role),
                Detail = "Seeded by DbCreator.",
            }));

            using var response = await batch.ExecuteAsync();

            if (response.IsSuccessStatusCode)
            {
                return;
            }

            // The API changed the access point in between: read it again and reapply.
            if (attempt >= 5 || (!response.Failed(HttpStatusCode.PreconditionFailed) && !response.Failed(HttpStatusCode.Conflict)))
            {
                response.ThrowIfFailed($"Seeding the access of '{accountId}' to '{key}'");
                throw new InvalidOperationException($"Seeding the access of '{accountId}' to '{key}' failed after {attempt} attempts: {response.StatusCode}.");
            }
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
