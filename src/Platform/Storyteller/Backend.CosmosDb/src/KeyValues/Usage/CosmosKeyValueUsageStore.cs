using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities.KeyValues;
using _42.Platform.Storyteller.KeyValues.Usage;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.KeyValues;

public class CosmosKeyValueUsageStore : IKeyValueUsageStore
{
    private const int MaxRetries = 5;

    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly KeyValueOptions _options;
    private readonly JsonSerializerSettings _serializerOptions;

    public CosmosKeyValueUsageStore(
        IContainerRepositoryProvider repositoryProvider,
        IOptions<KeyValueOptions> options,
        IOptions<JsonSerializerSettings> serializerOptions)
    {
        _repositoryProvider = repositoryProvider;
        _options = options.Value;
        _serializerOptions = serializerOptions.Value;
    }

    public static string GetPartitionKey(string project)
    {
        return $"{project}.usage";
    }

    public static string FormatDay(DateOnly day)
    {
        return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public Task IncrementAsync(string organization, string project, DateOnly day, IReadOnlyDictionary<string, long> counters, CancellationToken cancellationToken = default)
    {
        return UpdateAsync(organization, project, day, entity =>
        {
            var merged = new Dictionary<string, long>(entity.Counters, StringComparer.Ordinal);

            foreach (var (meter, count) in counters)
            {
                merged[meter] = merged.GetValueOrDefault(meter) + count;
            }

            return entity with { Counters = merged };
        }, cancellationToken);
    }

    public Task SetGaugesAsync(string organization, string project, DateOnly day, IReadOnlyDictionary<string, long> gauges, CancellationToken cancellationToken = default)
    {
        return UpdateAsync(organization, project, day, entity =>
        {
            var merged = new Dictionary<string, long>(entity.Gauges, StringComparer.Ordinal);

            foreach (var (meter, value) in gauges)
            {
                merged[meter] = value;
            }

            return entity with { Gauges = merged };
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<KeyValueUsageDay>> GetDaysAsync(string organization, string project, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE STARTSWITH(c.id, @prefix) AND c.Day >= @from AND c.Day <= @to ORDER BY c.Day")
            .WithParameter("@prefix", KeyValueUsageEntity.IdPrefix)
            .WithParameter("@from", FormatDay(from))
            .WithParameter("@to", FormatDay(to));

        var container = _repositoryProvider.GetOrganizationContainer(organization).Container;
        using var iterator = container.GetItemQueryIterator<KeyValueUsageEntity>(
            query,
            requestOptions: new QueryRequestOptions { PartitionKey = new PartitionKey(GetPartitionKey(project)) });
        var days = new List<KeyValueUsageDay>();

        while (iterator.HasMoreResults)
        {
            foreach (var entity in await iterator.ReadNextAsync(cancellationToken))
            {
                var values = new Dictionary<string, long>(entity.Counters, StringComparer.Ordinal);

                foreach (var (meter, value) in entity.Gauges)
                {
                    values[meter] = value;
                }

                days.Add(new KeyValueUsageDay
                {
                    Date = DateOnly.ParseExact(entity.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Values = values,
                });
            }
        }

        return days;
    }

    // Read, change, write with the ETag; the day document of a project is not contended enough for anything else.
    private async Task UpdateAsync(string organization, string project, DateOnly day, Func<KeyValueUsageEntity, KeyValueUsageEntity> change, CancellationToken cancellationToken)
    {
        var container = _repositoryProvider.GetOrganizationContainer(organization).Container;
        var partitionKeyValue = GetPartitionKey(project);
        var partitionKey = new PartitionKey(partitionKeyValue);
        var id = $"{KeyValueUsageEntity.IdPrefix}{FormatDay(day)}";

        for (var attempt = 0; attempt < MaxRetries; attempt++)
        {
            var (existing, etag) = await container.TryReadItemWithETagAsync(
                id,
                partitionKey,
                stream => stream.DeserializeNewtonsoft<KeyValueUsageEntity>(_serializerOptions),
                cancellationToken: cancellationToken);

            var entity = change(existing ?? new KeyValueUsageEntity
            {
                PartitionKey = partitionKeyValue,
                Id = id,
                TimeToLiveInSeconds = (int)Math.Max(60, _options.Usage.Retention.TotalSeconds),
                ProjectName = project,
                Day = FormatDay(day),
            });

            try
            {
                if (existing is null)
                {
                    await container.CreateItemAsync(entity, partitionKey, cancellationToken: cancellationToken);
                }
                else
                {
                    await container.ReplaceItemAsync(entity, id, partitionKey, new ItemRequestOptions { IfMatchEtag = etag }, cancellationToken);
                }

                return;
            }
            catch (CosmosException exception) when (exception.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.PreconditionFailed)
            {
                await Task.Delay(Random.Shared.Next(10, 50 * (attempt + 1)), cancellationToken);
            }
        }

        throw new KeyValueStorageException($"The usage of '{project}' for {FormatDay(day)} was changed concurrently {MaxRetries} times.", HttpStatusCode.Conflict);
    }
}
