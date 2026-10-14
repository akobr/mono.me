namespace _42.Platform.Storyteller.Transferring;

public interface IConfigurationTransferQueue
{
    Task EnqueueAsync(ConfigurationTransferMessage message, CancellationToken cancellationToken = default);
}
