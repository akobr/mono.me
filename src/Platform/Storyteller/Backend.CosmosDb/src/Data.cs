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
    // are created; an organization needs nothing more, its data lives in the shared container organizations. Each
    // membership is one batch of the access point, the account and its access audit record by System, so no role is
    // stored without its record.
    public static async Task SeedAccountAccessAsync(
        Container container,
        string accountId,
        string accountUserName,
        string accountName,
        IReadOnlyDictionary<string, AccountRole> roles,
        AccessAuditRecords? audit = null)
    {
        audit ??= AccessAuditRecords.Default;

        // An account without roles has no access, so nothing to record.
        if (roles.Count == 0)
        {
            await MergeAsync<AccountEntity>(
                container,
                accountId,
                account => account ?? new AccountEntity { Id = accountId, UserName = accountUserName, Name = accountName, AccessMap = [] });
            return;
        }

        foreach (var (key, role) in roles)
        {
            await MergeMembershipAsync(container, key, accountId, accountUserName, accountName, role, audit);
        }
    }

    // The membership of the account in one access point, on both documents, with its record, read and written with
    // optimistic concurrency like MergeAsync. A role both documents already hold writes and records nothing; a role only
    // one of them holds is repaired and recorded, since the account's map is what the role checks read.
    private static async Task MergeMembershipAsync(
        Container container,
        string key,
        string accountId,
        string accountUserName,
        string accountName,
        AccountRole role,
        AccessAuditRecords audit)
    {
        var partitionKey = new PartitionKey(AccessPointEntity.Partition);
        var pointId = $"{AccessPointEntity.IdPrefix}{key}";

        for (var attempt = 1; ; attempt++)
        {
            var (point, pointETag) = await ReadAsync<AccessPointEntity>(container, pointId, partitionKey);
            var (account, accountETag) = await ReadAsync<AccountEntity>(container, accountId, partitionKey);
            AccountRole? pointRole = point is not null && point.AccessMap.TryGetValue(accountId, out var stored) ? stored : null;
            AccountRole? accountRole = account is not null && account.AccessMap.TryGetValue(key, out var held) ? held : null;

            if (pointRole == role && accountRole == role)
            {
                return;
            }

            var batch = container.CreateTransactionalBatch(partitionKey);

            if (pointRole != role)
            {
                var pointMap = new Dictionary<string, AccountRole>(point?.AccessMap ?? []) { [accountId] = role };

                if (point is null)
                {
                    batch.CreateItem(new AccessPointEntity { Key = key, AccessMap = pointMap });
                }
                else
                {
                    batch.ReplaceItem(pointId, point with { AccessMap = pointMap }, new TransactionalBatchItemRequestOptions { IfMatchEtag = pointETag });
                }
            }
            else
            {
                // Unchanged, but guarded: the role was decided on this version, so a change of it fails the batch.
                batch.ReplaceItem(pointId, point!, new TransactionalBatchItemRequestOptions { IfMatchEtag = pointETag });
            }

            if (accountRole != role)
            {
                var accountMap = new Dictionary<string, AccountRole>(account?.AccessMap ?? []) { [key] = role };

                if (account is null)
                {
                    batch.CreateItem(new AccountEntity { Id = accountId, UserName = accountUserName, Name = accountName, AccessMap = accountMap });
                }
                else
                {
                    batch.ReplaceItem(accountId, account with { AccessMap = accountMap }, new TransactionalBatchItemRequestOptions { IfMatchEtag = accountETag });
                }
            }
            else
            {
                batch.ReplaceItem(accountId, account!, new TransactionalBatchItemRequestOptions { IfMatchEtag = accountETag });
            }

            var oldRole = pointRole != role ? pointRole : accountRole;
            var action = point is null
                ? AccessAuditAction.AccessPointCreated
                : oldRole is null ? AccessAuditAction.MemberGranted : AccessAuditAction.MemberRoleChanged;

            batch.CreateItem(audit.ForAccessPoint(key, new AccessAuditChange(action, AccessCaller.System)
            {
                Target = AccessAuditTarget.Account(accountId, accountName),
                OldValue = AccessAuditValues.Role(oldRole),
                NewValue = AccessAuditValues.Role(role),
                Detail = pointRole == role
                    ? "Repaired by DbCreator: the account did not hold the role of the access point."
                    : accountRole == role ? "Repaired by DbCreator: the access point did not hold the role of the account." : "Seeded by DbCreator.",
            }));

            using var response = await batch.ExecuteAsync();

            if (response.IsSuccessStatusCode)
            {
                return;
            }

            // The API changed the access point or the account in between: read both again and reapply.
            if (attempt >= 5 || (!response.Failed(HttpStatusCode.PreconditionFailed) && !response.Failed(HttpStatusCode.Conflict)))
            {
                response.ThrowIfFailed($"Seeding the access of '{accountId}' to '{key}'");
                throw new InvalidOperationException($"Seeding the access of '{accountId}' to '{key}' failed after {attempt} attempts: {response.StatusCode}.");
            }
        }
    }

    private static async Task<(T? Item, string? ETag)> ReadAsync<T>(Container container, string id, PartitionKey partitionKey)
        where T : class
    {
        try
        {
            var read = await container.ReadItemAsync<T>(id, partitionKey);
            return (read.Resource, read.ETag);
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return (null, null);
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
