using _42.Platform.Storyteller.Transferring;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Api.Transfers;

// The background half of the export and import jobs: the queue worker and the hourly purge of old archives.
// Both use the default connection (AzureWebJobsStorage), like the storage clients of the jobs. The queue keeps the
// storage name of the first spec (ViewTransferNames.Queue, configuration-transfers).
public class ViewTransferFunctions
{
    private readonly IViewTransferService _transfers;
    private readonly ILogger<ViewTransferFunctions> _logger;

    public ViewTransferFunctions(IViewTransferService transfers, ILogger<ViewTransferFunctions> logger)
    {
        _transfers = transfers;
        _logger = logger;
    }

    [Function(nameof(ProcessViewTransfer))]
    public async Task ProcessViewTransfer(
        [QueueTrigger(ViewTransferNames.Queue)] string message,
        CancellationToken cancellationToken)
    {
        if (!ViewTransferMessage.TryParse(message, out var transfer))
        {
            // a retry cannot make it valid, so it is dropped instead of ending in the poison queue
            _logger.LogError("A view transfer message is not valid and was dropped: {Message}", message);
            return;
        }

        await _transfers.ProcessAsync(transfer, cancellationToken);
    }

    // The runtime moves a message here after its last failed delivery. A worker that crashed, for example out of
    // memory, never ran the failure handling of the job, so the job is failed here and stops taking a slot.
    [Function(nameof(AbandonViewTransfer))]
    public async Task AbandonViewTransfer(
        [QueueTrigger(ViewTransferNames.PoisonQueue)] string message,
        CancellationToken cancellationToken)
    {
        if (!ViewTransferMessage.TryParse(message, out var transfer))
        {
            _logger.LogError("A poisoned view transfer message is not valid and was dropped: {Message}", message);
            return;
        }

        await _transfers.AbandonAsync(transfer, cancellationToken);
    }

    [Function(nameof(PurgeViewTransfers))]
    public async Task PurgeViewTransfers(
        [TimerTrigger("0 17 * * * *")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        var deleted = await _transfers.PurgeExpiredAsync(cancellationToken);

        if (deleted > 0)
        {
            _logger.LogInformation("{Count} expired transfer archives were deleted.", deleted);
        }
    }
}
