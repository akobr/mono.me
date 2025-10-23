using Orleans;

namespace _42.Crumble;

public interface ITimeActionsSchedulerGrain : IGrainWithStringKey
{
    Task StartScheduling();
}
