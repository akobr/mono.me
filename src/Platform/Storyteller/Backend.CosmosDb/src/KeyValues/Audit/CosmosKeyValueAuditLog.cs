using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities.KeyValues;
using _42.Platform.Storyteller.KeyValues.Audit;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.KeyValues;

public class CosmosKeyValueAuditLog : IKeyValueAuditLog
{
    private const int PageSize = 100;

    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly KeyValueOptions _options;
    private readonly JsonSerializerSettings _serializerOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CosmosKeyValueAuditLog> _logger;
    private readonly ConcurrentDictionary<BindingReadKey, int> _bindingReads = new();

    public CosmosKeyValueAuditLog(
        IContainerRepositoryProvider repositoryProvider,
        IOptions<KeyValueOptions> options,
        IOptions<JsonSerializerSettings> serializerOptions,
        TimeProvider? timeProvider = null,
        ILogger<CosmosKeyValueAuditLog>? logger = null)
    {
        _repositoryProvider = repositoryProvider;
        _options = options.Value;
        _serializerOptions = serializerOptions.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<CosmosKeyValueAuditLog>.Instance;
    }

    public static string GetPartitionKey(string project)
    {
        return $"{project}.audit";
    }

    public async Task WriteAsync(string organization, string project, KeyValueAuditEvent entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var container = _repositoryProvider.GetOrganizationContainer(organization).Container;
        var entity = ToEntity(project, entry);
        await container.CreateItemAsync(entity, new PartitionKey(entity.PartitionKey), cancellationToken: cancellationToken);
    }

    public void RecordBindingRead(string organization, string project, string view, string key, KeyValueProtection protection, KeyValueCaller caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var hour = _timeProvider.GetUtcNow().UtcDateTime.Date.AddHours(_timeProvider.GetUtcNow().UtcDateTime.Hour);
        var bucket = new BindingReadKey(organization, project, view, KeyValueRules.Normalize(key), protection, caller.Author, caller.Channel, hour);
        _bindingReads.AddOrUpdate(bucket, 1, (_, count) => count + 1);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        foreach (var bucket in _bindingReads.Keys.ToArray())
        {
            if (!_bindingReads.TryRemove(bucket, out var count) || count == 0)
            {
                continue;
            }

            try
            {
                await WriteAsync(bucket.Organization, bucket.Project, new KeyValueAuditEvent
                {
                    Id = NewId(bucket.Hour),
                    Time = new DateTimeOffset(bucket.Hour, TimeSpan.Zero),
                    View = bucket.View,
                    Key = bucket.Key,
                    Protection = bucket.Protection,
                    Operation = KeyValueAuditOperation.BindingRead,
                    Actor = bucket.Actor,
                    Channel = bucket.Channel,
                    Count = count,
                }, cancellationToken);
            }
            catch (Exception exception) when (exception is CosmosException or TimeoutException)
            {
                // put the count back for the next flush
                _bindingReads.AddOrUpdate(bucket, count, (_, current) => current + count);
                _logger.LogWarning(exception, "Could not write the aggregated binding reads of {Project}", bucket.Project);
            }
        }
    }

    public async Task<KeyValueAuditResponse> QueryAsync(string organization, string project, KeyValueAuditQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var text = "SELECT * FROM c WHERE STARTSWITH(c.id, @prefix)";

        if (!string.IsNullOrWhiteSpace(query.View))
        {
            text += " AND c.View = @view";
        }

        if (!string.IsNullOrWhiteSpace(query.Key))
        {
            text += " AND c.Key = @key";
        }

        if (query.Operation is not null)
        {
            text += " AND c.Operation = @operation";
        }

        if (query.From is not null)
        {
            text += " AND c.Time >= @from";
        }

        if (query.To is not null)
        {
            text += " AND c.Time <= @to";
        }

        text += " ORDER BY c.Time DESC";

        var definition = new QueryDefinition(text)
            .WithParameter("@prefix", KeyValueAuditEntity.IdPrefix)
            .WithParameter("@view", query.View)
            .WithParameter("@key", query.Key)
            .WithParameter("@operation", query.Operation?.ToString())
            .WithParameter("@from", query.From is null ? null : KeyValueAuditEntity.FormatTime(query.From.Value))
            .WithParameter("@to", query.To is null ? null : KeyValueAuditEntity.FormatTime(query.To.Value));

        var container = _repositoryProvider.GetOrganizationContainer(organization).Container;
        using var iterator = container.GetItemQueryIterator<KeyValueAuditEntity>(
            definition,
            string.IsNullOrWhiteSpace(query.ContinuationToken) ? null : query.ContinuationToken,
            new QueryRequestOptions { PartitionKey = new PartitionKey(GetPartitionKey(project)), MaxItemCount = PageSize });

        if (!iterator.HasMoreResults)
        {
            return new KeyValueAuditResponse { Items = [] };
        }

        var page = await iterator.ReadNextAsync(cancellationToken);
        var items = page.Select(ToEvent).ToList();

        return new KeyValueAuditResponse
        {
            Items = items,
            ContinuationToken = page.ContinuationToken,
            Count = items.Count,
        };
    }

    public static string NewId(DateTimeOffset time)
    {
        return $"{KeyValueAuditEntity.IdPrefix}{time.UtcDateTime.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)}.{Guid.NewGuid():N}";
    }

    private static string NewId(DateTime time)
    {
        return NewId(new DateTimeOffset(time, TimeSpan.Zero));
    }

    private static KeyValueAuditEvent ToEvent(KeyValueAuditEntity entity) => new()
    {
        Id = entity.Id,
        Time = DateTimeOffset.Parse(entity.Time, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
        View = entity.View,
        Key = entity.Key,
        Version = entity.Version,
        Protection = entity.Protection,
        Operation = entity.Operation,
        Actor = entity.Actor,
        Channel = entity.Channel,
        Count = entity.Count,
        CorrelationId = entity.CorrelationId,
        Detail = entity.Detail,
    };

    private KeyValueAuditEntity ToEntity(string project, KeyValueAuditEvent entry) => new()
    {
        PartitionKey = GetPartitionKey(project),
        Id = string.IsNullOrWhiteSpace(entry.Id) ? NewId(entry.Time) : entry.Id,
        TimeToLiveInSeconds = (int)Math.Max(60, _options.AuditRetention.TotalSeconds),
        ProjectName = project,
        Time = KeyValueAuditEntity.FormatTime(entry.Time),
        View = entry.View,
        Key = entry.Key,
        Version = entry.Version,
        Protection = entry.Protection,
        Operation = entry.Operation,
        Actor = entry.Actor,
        Channel = entry.Channel,
        Count = entry.Count,
        CorrelationId = entry.CorrelationId,
        Detail = entry.Detail,
    };

    private sealed record BindingReadKey(
        string Organization,
        string Project,
        string View,
        string Key,
        KeyValueProtection Protection,
        string Actor,
        KeyValueChannel Channel,
        DateTime Hour);
}
