using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities.Inbox;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Notifying;

// Items live in the partition "{project}.inbox.{view}" of the organization container and expire through their ttl.
// The list and the count name that full key, so they need no predicate on the organization.
public class CosmosViewInboxStore : IViewInboxStore
{
    private const string OpenState = nameof(ViewInboxItemState.Open);

    // What the list and the count share: the items of one inbox that the roles see. The project and the view are in
    // the partition already; the two predicates keep them honest for names with a dot in them, as the point read does.
    private const string VisibleItems =
        "FROM c WHERE c.PartitionKey = @partition AND STARTSWITH(c.id, @prefix) AND c.Project = @project AND c.View = @view "
        + "AND ARRAY_CONTAINS(@roles, c.MinimalRole)";

    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly JsonSerializerSettings _serializerSettings;

    public CosmosViewInboxStore(
        IContainerRepositoryProvider repositoryProvider,
        IOptions<JsonSerializerSettings> serializerSettings)
    {
        _repositoryProvider = repositoryProvider;
        _serializerSettings = serializerSettings.Value;
    }

    // Rounded up, so an item never expires before its time, and at least a second: Cosmos DB refuses a ttl of zero.
    public static int GetTimeToLive(TimeSpan timeToLive)
    {
        return Math.Max(1, (int)Math.Ceiling(timeToLive.TotalSeconds));
    }

    public async Task<ViewInboxRecord?> TryCreateAsync(ViewInboxRecord record, TimeSpan timeToLive, CancellationToken cancellationToken = default)
    {
        var repository = GetRepository(record.Organization);
        var entity = ViewInboxEntity.FromRecord(record, GetTimeToLive(timeToLive));

        try
        {
            var response = await repository.Container.CreateItemAsync(
                entity,
                repository.Key(entity.PartitionKey),
                new ItemRequestOptions { EnableContentResponseOnWrite = false },
                cancellationToken);

            return record with { ETag = response.ETag };
        }
        catch (CosmosException exception) when (exception.StatusCode == HttpStatusCode.Conflict)
        {
            return null;
        }
    }

    public async Task<ViewInboxRecord?> GetAsync(
        string organization,
        string project,
        string view,
        string id,
        CancellationToken cancellationToken = default)
    {
        var (entity, etag) = await GetRepository(organization).Container.TryReadItemWithETagAsync(
            ViewInboxEntity.GetId(id),
            PartitionKeys.GetCosmosInbox(organization, project, view),
            stream => stream.DeserializeNewtonsoft<ViewInboxEntity>(_serializerSettings),
            cancellationToken: cancellationToken);

        // the partition carries the project and the view; the check keeps them honest for names with a dot in them
        return entity is null || entity.Project != project || entity.View != view
            ? null
            : entity.ToRecord(etag);
    }

    public async Task<ViewInboxRecord?> TryReplaceAsync(ViewInboxRecord record, TimeSpan timeToLive, CancellationToken cancellationToken = default)
    {
        // without an ETag the replace would overwrite whatever another caller saved meanwhile
        if (record.ETag is null)
        {
            throw new ArgumentException("A replace needs the ETag of a point read or of a write; the records of a query have none.", nameof(record));
        }

        var repository = GetRepository(record.Organization);
        var entity = ViewInboxEntity.FromRecord(record, GetTimeToLive(timeToLive));

        try
        {
            var response = await repository.Container.ReplaceItemAsync(
                entity,
                entity.Id,
                repository.Key(entity.PartitionKey),
                new ItemRequestOptions { IfMatchEtag = record.ETag, EnableContentResponseOnWrite = false },
                cancellationToken);

            return record with { ETag = response.ETag };
        }
        catch (CosmosException exception) when (exception.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<ViewInboxRecord>> QueryAsync(
        string organization,
        string project,
        string view,
        ViewInboxFilter filter,
        IReadOnlyCollection<AccountRole> roles,
        string? before,
        int take,
        CancellationToken cancellationToken = default)
    {
        var partition = PartitionKeys.GetInbox(project, view);
        var text = new StringBuilder($"SELECT TOP @take * {VisibleItems}")
            .Append(filter == ViewInboxFilter.Open ? $" AND c.State = '{OpenState}'" : $" AND c.State != '{OpenState}'");

        if (before is not null)
        {
            text.Append(" AND c.id < @before");
        }

        text.Append(" ORDER BY c.id DESC");

        // the stored id has the prefix, the id of the item has not
        var query = WithVisibleItems(new QueryDefinition(text.ToString()), project, view, roles)
            .WithParameter("@take", take)
            .WithParameter("@before", before is null ? null : ViewInboxEntity.GetId(before));

        var repository = GetRepository(organization);
        var entities = new List<ViewInboxEntity>();

        using var iterator = repository.Container.GetItemQueryIterator<ViewInboxEntity>(
            query,
            requestOptions: repository.QueryOptions(partition, maxItemCount: take));

        // a page may hold fewer items than it was asked for, so the read goes on until it has them
        while (iterator.HasMoreResults && entities.Count < take)
        {
            entities.AddRange(await iterator.ReadNextAsync(cancellationToken));
        }

        return entities.Take(take).Select(entity => entity.ToRecord()).ToList();
    }

    public async Task<int> CountOpenAsync(
        string organization,
        string project,
        string view,
        IReadOnlyCollection<AccountRole> roles,
        CancellationToken cancellationToken = default)
    {
        var partition = PartitionKeys.GetInbox(project, view);
        var query = WithVisibleItems(
            new QueryDefinition($"SELECT VALUE COUNT(1) {VisibleItems} AND c.State = '{OpenState}'"),
            project,
            view,
            roles);

        var repository = GetRepository(organization);
        using var iterator = repository.Container.GetItemQueryIterator<int>(query, requestOptions: repository.QueryOptions(partition));

        var count = 0;

        while (iterator.HasMoreResults)
        {
            foreach (var value in await iterator.ReadNextAsync(cancellationToken))
            {
                count += value;
            }
        }

        return count;
    }

    // The roles as their names: the item stores its minimal role as a name.
    private static QueryDefinition WithVisibleItems(QueryDefinition query, string project, string view, IReadOnlyCollection<AccountRole> roles)
    {
        return query
            .WithParameter("@partition", PartitionKeys.GetInbox(project, view))
            .WithParameter("@prefix", ViewInboxEntity.IdPrefix)
            .WithParameter("@project", project)
            .WithParameter("@view", view)
            .WithParameter("@roles", roles.Select(role => role.ToString()).ToArray());
    }

    private OrganizationContainer GetRepository(string organization)
    {
        return _repositoryProvider.GetOrganizationContainer(organization);
    }
}
