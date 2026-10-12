using _42.Platform.Storyteller.KeyValues.Policy;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Api.Timers;

// Hourly: the pending re-wraps after a key rotation, and once a day the checks of the keys (new versions,
// the health of the customer's key).
public class KeyValueProtectionTimer
{
    private readonly IKeyValueMaintenanceService _maintenance;
    private readonly ILogger<KeyValueProtectionTimer> _logger;

    public KeyValueProtectionTimer(IKeyValueMaintenanceService maintenance, ILogger<KeyValueProtectionTimer> logger)
    {
        _maintenance = maintenance;
        _logger = logger;
    }

    [Function(nameof(KeyValueProtectionTimer))]
    public async Task Run([TimerTrigger("0 15 * * * *")] TimerInfo timer, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Key/value maintenance run started (late: {IsPastDue})", timer.IsPastDue);
        await _maintenance.RunAsync(cancellationToken);
    }
}
