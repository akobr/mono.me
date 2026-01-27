namespace _42.Crumble;

public class VolumeActionPoolingStartupGrain(
    IActionRegistry registry,
    IGrainFactory factory,
    IGrainContext context)
    : IGrainBase, IVolumeActionPoolingStartupGrain
{
    public IGrainContext GrainContext { get; } = context;

    public async Task StartPooling()
    {
        var allGroups = registry
            .GetActions<VolumeActionAttribute, string>()
            .GroupBy(d => d.Action.VolumeKey ?? "default");

        foreach (var group in allGroups)
        {
            var messageQueueGrain = factory.GetGrain<IVolumeActionPoolingGrain>($"VolumePooling:{group.Key}");
            await messageQueueGrain.StartPooling();
        }

        GrainContext.Deactivate(new DeactivationReason(DeactivationReasonCode.ActivationIdle, "The pooling for all volumes has been started."));
    }
}
