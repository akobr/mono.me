using Azure.Storage.Queues.Models;

namespace _42.Crumble;

public class MessageActionPoolingStartupGrain(
    IActionRegistry registry,
    IGrainFactory factory,
    IGrainContext context)
    : IGrainBase, IMessageActionPoolingStartupGrain
{
    public IGrainContext GrainContext { get; } = context;

    public async Task StartPooling()
    {
        var allGroups = registry
            .GetActions<MessageActionAttribute, QueueMessage>()
            .GroupBy(d => d.Action.QueueKey ?? "messages");

        foreach (var group in allGroups)
        {
            var messageQueueGrain = factory.GetGrain<IMessageActionPoolingGrain>($"MessagePooling:{group.Key}");
            await messageQueueGrain.StartPooling();
        }

        GrainContext.Deactivate(new DeactivationReason(DeactivationReasonCode.ActivationIdle, "The pooling for all queues has been started."));
    }
}
