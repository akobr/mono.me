using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.Entities;
using _42.Platform.Storyteller.KeyValues.Audit;
using _42.Platform.Storyteller.KeyValues.Usage;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.KeyValues;

// The flush of the in-memory counters, the daily snapshot of the stored keys, and the report.
public class CosmosKeyValueUsageService : IKeyValueUsageService
{
    public const string ReadsPricedItem = "reads";

    private readonly IKeyValueUsageRecorder _recorder;
    private readonly IKeyValueUsageStore _store;
    private readonly IKeyValueAuditLog _audit;
    private readonly IContainerFactory _containerFactory;
    private readonly IContainerRepositoryProvider _repositoryProvider;
    private readonly KeyValueOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CosmosKeyValueUsageService> _logger;

    public CosmosKeyValueUsageService(
        IKeyValueUsageRecorder recorder,
        IKeyValueUsageStore store,
        IKeyValueAuditLog audit,
        IContainerFactory containerFactory,
        IContainerRepositoryProvider repositoryProvider,
        IOptions<KeyValueOptions> options,
        TimeProvider? timeProvider = null,
        ILogger<CosmosKeyValueUsageService>? logger = null)
    {
        _recorder = recorder;
        _store = store;
        _audit = audit;
        _containerFactory = containerFactory;
        _repositoryProvider = repositoryProvider;
        _options = options.Value;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<CosmosKeyValueUsageService>.Instance;
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        var day = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        var increments = _recorder.Drain();

        foreach (var group in increments.GroupBy(increment => (increment.Organization, increment.Project)))
        {
            var counters = group
                .GroupBy(increment => increment.Meter, StringComparer.Ordinal)
                .ToDictionary(meter => meter.Key, meter => meter.Sum(increment => increment.Count), StringComparer.Ordinal);

            try
            {
                await _store.IncrementAsync(group.Key.Organization, group.Key.Project, day, counters, cancellationToken);
            }
            catch (Exception exception) when (exception is CosmosException or KeyValueStorageException or TimeoutException)
            {
                // the counts go back and are flushed next time; a crash loses at most one interval
                foreach (var (meter, count) in counters)
                {
                    _recorder.Add(group.Key.Organization, group.Key.Project, meter, count);
                }

                _logger.LogWarning(exception, "Could not flush the key/value usage of {Organization}.{Project}", group.Key.Organization, group.Key.Project);
            }
        }

        await _audit.FlushAsync(cancellationToken);
    }

    public async Task TakeSnapshotAsync(DateOnly day, CancellationToken cancellationToken = default)
    {
        var prefix = CosmosConstants.OrganizationContainerNamePrefix;

        foreach (var containerName in await _containerFactory.GetContainerNamesAsync())
        {
            if (!containerName.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            var organization = containerName[prefix.Length..];
            var gaugesPerProject = new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);

            await foreach (var row in QueryKeysAsync(organization, cancellationToken))
            {
                var gauges = GetOrAdd(gaugesPerProject, row.Project);
                var meter = KeyValueUsageMeters.KeysOf(row.Protection);
                gauges[meter] = gauges.GetValueOrDefault(meter) + row.Count;
                gauges[KeyValueUsageMeters.StorageBytes] = gauges.GetValueOrDefault(KeyValueUsageMeters.StorageBytes) + row.Bytes;
            }

            await foreach (var row in QueryReferencesAsync(organization, cancellationToken))
            {
                GetOrAdd(gaugesPerProject, row.Project)[KeyValueUsageMeters.KeysReference] = row.Count;
            }

            await foreach (var row in QueryVersionsAsync(organization, cancellationToken))
            {
                var gauges = GetOrAdd(gaugesPerProject, row.Project);
                gauges[KeyValueUsageMeters.Versions] = row.Count;
                gauges[KeyValueUsageMeters.StorageBytes] = gauges.GetValueOrDefault(KeyValueUsageMeters.StorageBytes) + row.Bytes;
            }

            foreach (var (project, gauges) in gaugesPerProject)
            {
                foreach (var meter in KeyValueUsageMeters.Gauges)
                {
                    gauges.TryAdd(meter, 0);
                }

                await _store.SetGaugesAsync(organization, project, day, gauges, cancellationToken);
            }
        }
    }

    public async Task<KeyValueUsageReport> GetReportAsync(string organization, string project, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new InvalidInputException("The end of the range is before its start.", ErrorCodes.InvalidKey);
        }

        if (to.DayNumber - from.DayNumber > 400)
        {
            throw new InvalidInputException("A usage report covers at most 400 days.", ErrorCodes.InvalidKey);
        }

        var days = await _store.GetDaysAsync(organization, project, from, to, cancellationToken);
        var rangeDays = to.DayNumber - from.DayNumber + 1;
        var totals = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var day in days)
        {
            foreach (var (meter, value) in day.Values)
            {
                totals[meter] = totals.GetValueOrDefault(meter) + value;
            }
        }

        // a gauge total is the average over the range: key-months for a calendar month
        foreach (var meter in KeyValueUsageMeters.Gauges)
        {
            if (totals.TryGetValue(meter, out var sum))
            {
                totals[meter] = Math.Round(sum / rangeDays, 4);
            }
        }

        return new KeyValueUsageReport
        {
            From = from,
            To = to,
            Totals = totals,
            Days = days,
            EstimatedCharges = Estimate(totals),
        };
    }

    // Pricing entries are per meter unit (key-month for key meters, 10,000 for reads); "reads" prices bindings and API reads together.
    private IReadOnlyDictionary<string, decimal>? Estimate(IReadOnlyDictionary<string, decimal> totals)
    {
        if (_options.Pricing.Count == 0)
        {
            return null;
        }

        var charges = new Dictionary<string, decimal>(StringComparer.Ordinal);

        foreach (var (item, price) in _options.Pricing)
        {
            decimal quantity;

            if (item == ReadsPricedItem)
            {
                quantity = (totals.GetValueOrDefault(KeyValueUsageMeters.ReadsBinding) + totals.GetValueOrDefault(KeyValueUsageMeters.ReadsApi)) / 10_000m;
            }
            else
            {
                quantity = totals.GetValueOrDefault(item);
            }

            if (quantity > 0)
            {
                charges[item] = Math.Round(quantity * price, 2);
            }
        }

        return charges;
    }

    private static Dictionary<string, long> GetOrAdd(Dictionary<string, Dictionary<string, long>> gaugesPerProject, string project)
    {
        if (!gaugesPerProject.TryGetValue(project, out var gauges))
        {
            gauges = new Dictionary<string, long>(StringComparer.Ordinal);
            gaugesPerProject[project] = gauges;
        }

        return gauges;
    }

    private IAsyncEnumerable<KeyGaugeRow> QueryKeysAsync(string organization, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                "SELECT c.ProjectName AS Project, c.Content.Protection AS Protection, COUNT(1) AS Count, SUM(c.Content.Length) AS Bytes FROM c "
                + "WHERE CONTAINS(c.PartitionKey, @infix) AND STARTSWITH(c.id, @prefix) AND NOT IS_DEFINED(c.DeletedAt) "
                + "GROUP BY c.ProjectName, c.Content.Protection")
            .WithParameter("@infix", $".{PartitionKeys.KeyValuesPartitionInfix}.")
            .WithParameter("@prefix", $"{EntityIdPrefixTypes.KeyValue}.");

        return QueryAsync<KeyGaugeRow>(organization, query, cancellationToken);
    }

    private IAsyncEnumerable<ProjectCountRow> QueryReferencesAsync(string organization, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                "SELECT c.ProjectName AS Project, COUNT(1) AS Count, SUM(c.Content.Length) AS Bytes FROM c "
                + "WHERE CONTAINS(c.PartitionKey, @infix) AND STARTSWITH(c.id, @prefix) AND NOT IS_DEFINED(c.DeletedAt) "
                + "AND (c.Content.ContentType = @reference OR c.Content.ContentType = @appConfiguration) GROUP BY c.ProjectName")
            .WithParameter("@infix", $".{PartitionKeys.KeyValuesPartitionInfix}.")
            .WithParameter("@prefix", $"{EntityIdPrefixTypes.KeyValue}.")
            .WithParameter("@reference", KeyValueContentTypes.SecretReference)
            .WithParameter("@appConfiguration", KeyValueContentTypes.AppConfigurationKeyVaultReference);

        return QueryAsync<ProjectCountRow>(organization, query, cancellationToken);
    }

    private IAsyncEnumerable<ProjectCountRow> QueryVersionsAsync(string organization, CancellationToken cancellationToken)
    {
        var query = new QueryDefinition(
                "SELECT c.ProjectName AS Project, COUNT(1) AS Count, SUM(c.Content.Length) AS Bytes FROM c "
                + "WHERE CONTAINS(c.PartitionKey, @infix) AND STARTSWITH(c.id, @prefix) GROUP BY c.ProjectName")
            .WithParameter("@infix", $".{PartitionKeys.KeyValuesPartitionInfix}.")
            .WithParameter("@prefix", $"{EntityIdPrefixTypes.KeyValueVersion}.");

        return QueryAsync<ProjectCountRow>(organization, query, cancellationToken);
    }

    private async IAsyncEnumerable<T> QueryAsync<T>(string organization, QueryDefinition query, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var container = _repositoryProvider.GetOrganizationContainer(organization).Container;
        using var iterator = container.GetItemQueryIterator<T>(query);

        while (iterator.HasMoreResults)
        {
            foreach (var row in await iterator.ReadNextAsync(cancellationToken))
            {
                yield return row;
            }
        }
    }

    private sealed record KeyGaugeRow(string Project, KeyValueProtection Protection, long Count, long Bytes);

    private sealed record ProjectCountRow(string Project, long Count, long Bytes);
}
