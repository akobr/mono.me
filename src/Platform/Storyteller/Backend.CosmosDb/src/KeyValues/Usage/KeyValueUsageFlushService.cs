using System;
using System.Threading;
using System.Threading.Tasks;
using _42.Platform.Storyteller.KeyValues.Usage;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace _42.Platform.Storyteller.KeyValues;

// Writes the in-memory usage counters and the aggregated binding reads every FlushInterval, and once more on shutdown.
public sealed class KeyValueUsageFlushService : BackgroundService
{
    private readonly IKeyValueUsageService _usage;
    private readonly TimeSpan _interval;
    private readonly ILogger<KeyValueUsageFlushService> _logger;

    public KeyValueUsageFlushService(
        IKeyValueUsageService usage,
        IOptions<KeyValueOptions> options,
        ILogger<KeyValueUsageFlushService>? logger = null)
    {
        _usage = usage;
        _interval = options.Value.Usage.FlushInterval > TimeSpan.Zero ? options.Value.Usage.FlushInterval : TimeSpan.FromSeconds(60);
        _logger = logger ?? NullLogger<KeyValueUsageFlushService>.Instance;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await FlushAsync(CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }

        await FlushAsync(CancellationToken.None);
    }

    private async Task FlushAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _usage.FlushAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "The key/value usage flush failed; the counts are kept for the next one");
        }
    }
}
