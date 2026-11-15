#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Accessing;
using _42.Platform.Storyteller.Entities.Access;
using FluentAssertions;
using Microsoft.Azure.Cosmos;

namespace _42.Platform.Storyteller.Backend.CosmosDb.UnitTests;

// The access audit records as stored, read straight from their partitions: core "access" (memberships, access points,
// policies, filtered on the organization as the read side will), and (organization, "{project}.access") or
// (organization, "{project}.federation") of the container organizations (machines, shared certificates, trusted issuers).
internal static class StoredAccessAudit
{
    // The ttl of every record with the default retention (AccessAudit:RetentionDays, two years).
    public static readonly int TimeToLiveInSeconds = new AccessAuditOptions().RetentionDays * 24 * 60 * 60;

    // The records of one access point, an organization or organization.project, oldest first.
    public static Task<IReadOnlyList<AccessAuditEntity>> ReadCoreAsync(IContainerRepositoryProvider repositories, string accessPointKey)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE STARTSWITH(c.id, @prefix) AND c.Organization = @organization AND c.AccessPointKey = @key")
            .WithParameter("@prefix", AccessAuditEntity.IdPrefix)
            .WithParameter("@organization", accessPointKey.Split('.', 2)[0])
            .WithParameter("@key", accessPointKey);

        return ReadAsync(
            repositories.GetCore().Container,
            query,
            new QueryRequestOptions { PartitionKey = new PartitionKey(AccessPointEntity.Partition) });
    }

    // The records of one partition of the container organizations, oldest first.
    public static Task<IReadOnlyList<AccessAuditEntity>> ReadAsync(IContainerRepositoryProvider repositories, string organization, string partition)
    {
        var repository = repositories.GetOrganizationContainer(organization);
        var query = new QueryDefinition("SELECT * FROM c WHERE STARTSWITH(c.id, @prefix)")
            .WithParameter("@prefix", AccessAuditEntity.IdPrefix);

        return ReadAsync(repository.Container, query, repository.QueryOptions(partition));
    }

    // The records that the change added to what read returns.
    public static async Task<IReadOnlyList<AccessAuditEntity>> AddedByAsync(Func<Task<IReadOnlyList<AccessAuditEntity>>> read, Func<Task> change)
    {
        var before = (await read()).Select(record => record.Id).ToHashSet(StringComparer.Ordinal);
        await change();
        return (await read()).Where(record => !before.Contains(record.Id)).ToList();
    }

    // Every field of the record as expected, the ttl of the default retention, and an id that starts with the time.
    public static void ShouldMatch(this AccessAuditEntity record, ExpectedAccessAudit expected)
    {
        var segments = expected.AccessPointKey.Split('.', 2);

        record.Should().BeEquivalentTo(new
        {
            expected.PartitionKey,
            Organization = segments[0],
            Project = segments.Length == 2 ? segments[1] : null,
            expected.AccessPointKey,
            expected.Action,
            expected.Caller.Actor,
            expected.Target,
            expected.OldValue,
            expected.NewValue,
            expected.Detail,
            expected.Caller.CorrelationId,
            TimeToLiveInSeconds,
        });

        var time = record.ToEntry().Time;
        time.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(5));
        record.Id.Should().StartWith($"{AccessAuditEntity.IdPrefix}{time.UtcDateTime.ToString("yyyyMMddHHmmssfffffff", CultureInfo.InvariantCulture)}.");
    }

    private static async Task<IReadOnlyList<AccessAuditEntity>> ReadAsync(Container container, QueryDefinition query, QueryRequestOptions options)
    {
        var records = new List<AccessAuditEntity>();
        using var iterator = container.GetItemQueryIterator<AccessAuditEntity>(query, requestOptions: options);

        while (iterator.HasMoreResults)
        {
            records.AddRange(await iterator.ReadNextAsync());
        }

        // the id order is the time order
        return records.OrderBy(record => record.Id, StringComparer.Ordinal).ToList();
    }
}
