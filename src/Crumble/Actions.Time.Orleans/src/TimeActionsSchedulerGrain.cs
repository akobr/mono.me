using System.Diagnostics;
using Cronos;
using Microsoft.Extensions.Logging;
using Orleans.Timers;

namespace _42.Crumble;

[GenerateSerializer]
public record SchedulerState(
    [property: Id(0)] DateTime? LastSchedulingAt);

public class TimeActionsSchedulerGrain(
    IActionRegistry registry,
    IReminderRegistry reminders,
    IPersistentState<SchedulerState> persistence,
    IGrainContext context,
    IServiceProvider serviceProvider,
    ILogger<TimeActionsSchedulerGrain> logger)
    : IGrainBase, ITimeActionsSchedulerGrain, IRemindable
{
    public IGrainContext GrainContext { get; } = context;

    public Task StartScheduling()
    {
        reminders.RegisterOrUpdateReminder(GrainContext.GrainId, "TimeActionsScheduling", TimeSpan.Zero, TimeSpan.FromMinutes(10));
        return Task.CompletedTask;
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        var lastSchedulingAt = persistence.State.LastSchedulingAt ?? status.CurrentTickTime.Add(-status.Period);
        var definitions = registry
            .GetActions<TimeActionAttribute, DateTime>()
            .GroupBy(d => d.Action.TimeZone ?? "UTC");
        var tasks = new List<Task>();

        foreach (var zoneGroup in definitions)
        {
            if (!TimeZoneInfo.TryFindSystemTimeZoneById(zoneGroup.Key, out var zone))
            {
                logger.LogWarning("Unable to find time zone {TimeZoneId}", zoneGroup.Key);
                continue;
            }

            var startTimeUtc = lastSchedulingAt.ToUniversalTime().Add(zone.BaseUtcOffset);
            var endTimeUtc = status.CurrentTickTime.ToUniversalTime().Add(zone.BaseUtcOffset);

            foreach (var model in zoneGroup)
            {
                if (!CronExpression.TryParse(model.Action.Cron, CronFormat.Standard, out var cron))
                {
                    logger.LogWarning("Unable to parse cron expression '{Cron}' for crumb of key '{CrumbKey}'.", model.Action.Cron, model.CrumbKey);
                    continue;
                }

                var occurrences = cron.GetOccurrences(startTimeUtc, endTimeUtc, true, false);
                tasks.Add(ExecuteOccurrencesSynchronously(occurrences, model));
            }
        }

        try
        {
            // TODO: [P1] this need to be just async trigger
            await Task.WhenAll(tasks);
        }
        finally
        {
            persistence.State = persistence.State with { LastSchedulingAt = status.CurrentTickTime };
            await persistence.WriteStateAsync();
        }
    }

    private async Task ExecuteOccurrencesSynchronously(
        IEnumerable<DateTime> occurrences,
        ActionModel<TimeActionAttribute, DateTime> model)
    {
        foreach (var occurrenceUtc in occurrences)
        {
            using var activity = Telemetry.ActivitySource.StartActivity("crumble.action", ActivityKind.Producer);

            if (activity is not null)
            {
                activity.AddTag("actionType", "time");
                activity.AddTag("triggerTimeUtc", occurrenceUtc);
            }

            try
            {
                await model.Executor(serviceProvider, occurrenceUtc);
                Telemetry.Actions.Add(
                    1,
                    new KeyValuePair<string, object?>("outcome", "success"),
                    new KeyValuePair<string, object?>("actionType", "time"));
            }
            catch (Exception)
            {
                Telemetry.Actions.Add(
                    1,
                    new KeyValuePair<string, object?>("outcome", "failure"),
                    new KeyValuePair<string, object?>("actionType", "time"));
                throw;
            }
            finally
            {
                Telemetry.Actions.Add(
                    1,
                    new KeyValuePair<string, object?>("outcome", "engine"),
                    new KeyValuePair<string, object?>("actionType", "time"));
            }
        }
    }
}
