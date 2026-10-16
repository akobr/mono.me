namespace _42.Platform.Storyteller.Transferring;

public interface IViewTransferQueue
{
    Task EnqueueAsync(ViewTransferMessage message, CancellationToken cancellationToken = default);
}
