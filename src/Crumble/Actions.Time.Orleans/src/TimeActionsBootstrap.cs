namespace _42.Crumble;

public class TimeActionsBootstrap(IGrainFactory factory) : IStartupTask
{
    public Task Execute(CancellationToken cancellationToken)
    {
        var scheduler = factory.GetGrain<ITimeActionsSchedulerGrain>("default");
        return scheduler.StartScheduling();
    }
}
