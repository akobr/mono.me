using System.Collections.Concurrent;

namespace _42.Platform.Storyteller.KeyValues.Usage;

// Counts operations in memory; the flush writes them to the day documents.
public interface IKeyValueUsageRecorder
{
    void Add(string organization, string project, string meter, long count = 1);

    // Takes the counts recorded so far.
    IReadOnlyList<KeyValueUsageIncrement> Drain();
}

public sealed record KeyValueUsageIncrement(string Organization, string Project, string Meter, long Count);

public sealed class KeyValueUsageRecorder : IKeyValueUsageRecorder
{
    private readonly ConcurrentDictionary<(string Organization, string Project, string Meter), long> _counters = new();

    public void Add(string organization, string project, string meter, long count = 1)
    {
        if (count == 0)
        {
            return;
        }

        _counters.AddOrUpdate((organization, project, meter), count, (_, current) => current + count);
    }

    public IReadOnlyList<KeyValueUsageIncrement> Drain()
    {
        var drained = new List<KeyValueUsageIncrement>();

        foreach (var key in _counters.Keys.ToArray())
        {
            if (_counters.TryRemove(key, out var count) && count != 0)
            {
                drained.Add(new KeyValueUsageIncrement(key.Organization, key.Project, key.Meter, count));
            }
        }

        return drained;
    }
}

// The day documents of a project.
public interface IKeyValueUsageStore
{
    Task IncrementAsync(string organization, string project, DateOnly day, IReadOnlyDictionary<string, long> counters, CancellationToken cancellationToken = default);

    Task SetGaugesAsync(string organization, string project, DateOnly day, IReadOnlyDictionary<string, long> gauges, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KeyValueUsageDay>> GetDaysAsync(string organization, string project, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}

// The flush, the daily snapshot and the report.
public interface IKeyValueUsageService
{
    Task FlushAsync(CancellationToken cancellationToken = default);

    // The gauges of every project for one day, from the stored items.
    Task TakeSnapshotAsync(DateOnly day, CancellationToken cancellationToken = default);

    Task<KeyValueUsageReport> GetReportAsync(string organization, string project, DateOnly from, DateOnly to, CancellationToken cancellationToken = default);
}
