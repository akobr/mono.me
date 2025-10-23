using Orleans;
using Orleans.Runtime;
using Orleans.Timers;

namespace _42.Crumble;

public class TimeActionsSchedulerGrain(
    IActionRegistry registry,
    IReminderRegistry reminders,
    IGrainContext context)
    : IGrainBase, ITimeActionsSchedulerGrain, IRemindable
{
    public IGrainContext GrainContext { get; } = context;

    public Task StartScheduling()
    {
        reminders.RegisterOrUpdateReminder(GrainContext.GrainId, "TimeActionsScheduling", TimeSpan.Zero, TimeSpan.FromMinutes(10));
        return Task.CompletedTask;
    }

    public Task ReceiveReminder(string reminderName, TickStatus status)
    {
        // TODO: do the implementation of the scheduler
        var definitions = registry
            .GetActions<TimeActionAttribute>()
            .GroupBy(d => d.Model.TimeZone ?? "UTC");

        return Task.CompletedTask;
    }
}
