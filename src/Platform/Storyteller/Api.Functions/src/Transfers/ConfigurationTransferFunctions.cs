using _42.Platform.Storyteller.Transferring;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Api.Transfers;

// The background half of the export and import jobs: the queue worker and the hourly purge of old archives.
// Both use the default connection (AzureWebJobsStorage), like the storage clients of the jobs.
public class ConfigurationTransferFunctions
{
    private readonly IViewTransferService _transfers;
    private readonly ILogger<ConfigurationTransferFunctions> _logger;

    public ConfigurationTransferFunctions(IViewTransferService transfers, ILogger<ConfigurationTransferFunctions> logger)
    {
        _transfers = transfers;
        _logger = logger;
    }

    [Function(nameof(ProcessConfigurationTransfer))]
    public async Task ProcessConfigurationTransfer(
        [QueueTrigger(ViewTransferNames.Queue)] string message,
        CancellationToken cancellationToken)
    {
        if (!ViewTransferMessage.TryParse(message, out var transfer))
        {
            // a retry cannot make it valid, so it is dropped instead of ending in the poison queue
            _logger.LogError("A configuration transfer message is not valid and was dropped: {Message}", message);
            return;
        }

        await _transfers.ProcessAsync(transfer, cancellationToken);
    }

    [Function(nameof(PurgeConfigurationTransfers))]
    public async Task PurgeConfigurationTransfers(
        [TimerTrigger("0 17 * * * *")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var deleted = await _transfers.PurgeExpiredAsync(cancellationToken);

        if (deleted > 0)
        {
            _logger.LogInformation("{Count} expired configuration archives were deleted.", deleted);
        }
    }
}
