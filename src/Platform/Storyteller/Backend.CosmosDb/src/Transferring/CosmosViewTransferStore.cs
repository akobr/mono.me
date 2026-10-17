using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities.Transfers;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Transferring;

// Jobs live in the partition "{project}.transfer" of the organization container and expire through their ttl.
public class CosmosViewTransferStore : IViewTransferStore
{
    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly JsonSerializerSettings _serializerSettings;
    private readonly ViewTransferOptions _options;

    public CosmosViewTransferStore(
        IContainerRepositoryProvider repositoryProvider,
        IOptions<JsonSerializerSettings> serializerSettings,
        IOptions<ViewTransferOptions> options)
    {
        _repositoryProvider = repositoryProvider;
        _serializerSettings = serializerSettings.Value;
        _options = options.Value;
    }

    public static string GetId(string view, ViewTransferKind kind, string id)
    {
        return $"{view}.{(kind == ViewTransferKind.Export ? "exp" : "imp")}.{id}";
    }

    public async Task<ViewTransferRecord> CreateAsync(ViewTransferRecord record, CancellationToken cancellationToken = default)
    {
        var container = GetContainer(record.Organization);
        var entity = ToEntity(record);
        var response = await container.CreateItemAsync(
            entity,
            new PartitionKey(entity.PartitionKey),
            new ItemRequestOptions { EnableContentResponseOnWrite = false },
            cancellationToken);

        return record with { ETag = response.ETag };
    }

    public async Task<ViewTransferRecord?> GetAsync(
        string organization,
        string project,
        string view,
        ViewTransferKind kind,
        string id,
        CancellationToken cancellationToken = default)
    {
        var (entity, etag) = await GetContainer(organization).TryReadItemWithETagAsync(
            GetId(view, kind, id),
            PartitionKeys.GetCosmosTransfer(project),
            stream => stream.DeserializeNewtonsoft<ViewTransferEntity>(_serializerSettings),
            cancellationToken: cancellationToken);

        // the id carries the view, the check keeps the kind honest as well
        return entity is null || entity.Kind != kind || entity.ViewName != view
            ? null
            : ToRecord(organization, entity, etag);
    }

    public async Task<ViewTransferRecord?> TryReplaceAsync(ViewTransferRecord record, CancellationToken cancellationToken = default)
    {
        var entity = ToEntity(record);

        try
        {
            var response = await GetContainer(record.Organization).ReplaceItemAsync(
                entity,
                entity.Id,
                new PartitionKey(entity.PartitionKey),
                new ItemRequestOptions { IfMatchEtag = record.ETag, EnableContentResponseOnWrite = false },
                cancellationToken);

            return record with { ETag = response.ETag };
        }
        catch (CosmosException exception) when (exception.StatusCode is HttpStatusCode.PreconditionFailed or HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<int> CountActiveAsync(string organization, string project, DateTimeOffset changedSince, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition(
                "SELECT VALUE COUNT(1) FROM c WHERE c.PartitionKey = @partition "
                + "AND (c.Status = @queued OR c.Status = @running) AND c._ts >= @since")
            .WithParameter("@partition", PartitionKeys.GetTransfer(project))
            .WithParameter("@queued", nameof(ViewTransferStatus.Queued))
            .WithParameter("@running", nameof(ViewTransferStatus.Running))
            .WithParameter("@since", changedSince.ToUnixTimeSeconds());

        using var iterator = GetContainer(organization).GetItemQueryIterator<int>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = PartitionKeys.GetCosmosTransfer(project) });

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

    private Container GetContainer(string organization)
    {
        return _repositoryProvider.GetOrganizationContainer(organization).Container;
    }

    private ViewTransferEntity ToEntity(ViewTransferRecord record)
    {
        return new ViewTransferEntity
        {
            Id = GetId(record.View, record.Kind, record.Id),
            PartitionKey = PartitionKeys.GetTransfer(record.Project),
            ProjectName = record.Project,
            ViewName = record.View,
            TransferId = record.Id,
            Kind = record.Kind,
            Status = record.Status,
            Export = record.Export,
            Import = record.Import,
            TimeToLiveInSeconds = (int)_options.Retention.TotalSeconds,
        };
    }

    private static ViewTransferRecord ToRecord(string organization, ViewTransferEntity entity, string? etag)
    {
        return new ViewTransferRecord
        {
            Organization = organization,
            Project = entity.ProjectName,
            View = entity.ViewName,
            Kind = entity.Kind,
            Id = entity.TransferId,
            Export = entity.Export,
            Import = entity.Import,
            ETag = etag,
        };
    }
}
