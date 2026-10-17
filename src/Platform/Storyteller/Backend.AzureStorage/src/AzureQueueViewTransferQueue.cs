using Azure.Storage.Queues;

namespace _42.Platform.Storyteller.Transferring;

// Jobs as JSON messages of a storage queue, created on first use.
public sealed class AzureQueueViewTransferQueue : IViewTransferQueue
{
    private readonly QueueClient _queue;
    private volatile bool _isQueueReady;

    public AzureQueueViewTransferQueue(QueueClient queue)
    {
        _queue = queue;
    }

    public async Task EnqueueAsync(ViewTransferMessage message, CancellationToken cancellationToken = default)
    {
        if (!_isQueueReady)
        {
            await _queue.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
            _isQueueReady = true;
        }

        await _queue.SendMessageAsync(message.ToJson(), cancellationToken);
    }
}
