using _42.Platform.Storyteller.KeyValues.Usage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Api.Timers;

// Daily at 00:10 UTC: the key and version counts of every project, written into the previous day's usage.
public class KeyValueUsageSnapshotTimer
{
    private readonly IKeyValueUsageService _usage;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<KeyValueUsageSnapshotTimer> _logger;

    public KeyValueUsageSnapshotTimer(IKeyValueUsageService usage, TimeProvider timeProvider, ILogger<KeyValueUsageSnapshotTimer> logger)
    {
        _usage = usage;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    [Function(nameof(KeyValueUsageSnapshotTimer))]
    public async Task Run([TimerTrigger("0 10 0 * * *")] TimerInfo timer, CancellationToken cancellationToken)
    {
        var day = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime).AddDays(-1);
        _logger.LogInformation("Key/value usage snapshot for {Day} started", day);
        await _usage.TakeSnapshotAsync(day, cancellationToken);
    }
}
