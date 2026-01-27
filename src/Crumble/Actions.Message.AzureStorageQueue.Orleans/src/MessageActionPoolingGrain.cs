using System.Diagnostics;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Timers;

namespace _42.Crumble;

[GenerateSerializer]
public record MessagePoolingState(
    [property: Id(0)] DateTime? LastPollingAt,
    [property: Id(1)] TimeSpan? CurrentPoolingInterval);

public class MessageActionPoolingGrain : IGrainBase, IMessageActionPoolingGrain, IRemindable
{
    private readonly string _queueKey;
    private readonly QueueClient _mainQueue;
    private readonly QueueClient _poisonQueue;

    private readonly IActionRegistry _registry;
    private readonly IReminderRegistry _reminders;
    private readonly ITimerRegistry _timers;
    private readonly IPersistentState<MessagePoolingState> _persistence;
    private readonly IOptionsMonitor<MessagePoolingSettings> _optionsMonitor;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MessageActionPoolingGrain> _logger;

    private IGrainReminder? _reminder;
    private IGrainTimer? _timer;

    public MessageActionPoolingGrain(
        IActionRegistry registry,
        IReminderRegistry reminders,
        ITimerRegistry timers,
        QueueServiceClient client,
        IPersistentState<MessagePoolingState> persistence,
        IOptionsMonitor<MessagePoolingSettings> optionsMonitor,
        IGrainContext context,
        IServiceProvider serviceProvider,
        ILogger<MessageActionPoolingGrain> logger)
    {
        _registry = registry;
        _reminders = reminders;
        _timers = timers;
        _persistence = persistence;
        _optionsMonitor = optionsMonitor;
        _serviceProvider = serviceProvider;
        _logger = logger;

        // MessagePooling:QueueKey
        var grainKey = context.GrainId.ToString();

        if (grainKey.Length < 16 || grainKey[14] != ':')
        {
            throw new ArgumentException("Invalid grain key.", nameof(context.GrainId));
        }

        _queueKey = grainKey[15..];
        _mainQueue = client.GetQueueClient(_queueKey);
        _poisonQueue = client.GetQueueClient($"{_queueKey}-poison");
        GrainContext = context;
    }

    public IGrainContext GrainContext { get; }

    public Task OnDeactivateAsync(DeactivationReason reason, CancellationToken token)
    {
        _timer?.Dispose();
        return Task.CompletedTask;
    }

    public async Task StartPooling()
    {
        var settings = _optionsMonitor.CurrentValue;

        _reminder = await _reminders.RegisterOrUpdateReminder(
            GrainContext.GrainId,
            "MessagePooling",
            TimeSpan.Zero,
            settings.PoolingMaxInterval);
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        var isCancelled = await PullMessages(status.CurrentTickTime, CancellationToken.None);

        if (isCancelled)
        {
            _reminder ??= await _reminders.GetReminder(GrainContext.GrainId, reminderName);
            _reminders.UnregisterReminder(GrainContext.GrainId, _reminder);
            GrainContext.Deactivate(new DeactivationReason(DeactivationReasonCode.ActivationIdle, "There is no receiver."));
            _logger.LogWarning("There is no receiver for a message from queue '{QueueKey}'.", _queueKey);
        }
    }

    private async Task<bool> PullMessages(DateTime currentTickTime, CancellationToken cancellationToken = default)
    {
        var allGroups = _registry
            .GetActions<MessageActionAttribute, QueueMessage>()
            .GroupBy(d => d.Action.QueueKey ?? "messages");

        var queueGroup = allGroups.FirstOrDefault(g => g.Key == _queueKey);

        if (queueGroup is null) // no receiver
        {
            return true;
        }

        var receivers = queueGroup.ToArray();

        if (receivers.Length > 1) // multiple receivers
        {
            _logger.LogInformation("There are multiple receivers for a message from queue '{QueueKey}'.", _queueKey);
        }

        var settings = _optionsMonitor.CurrentValue;
        var response = await _mainQueue.ReceiveMessagesAsync(settings.BatchSize, settings.VisibilityTimeout, cancellationToken);
        var state = _persistence.State with
        {
            LastPollingAt = currentTickTime,
        };

        var poolingInterval = state.CurrentPoolingInterval ?? settings.PoolingMinInterval;

        if (!response.HasValue || response.Value.Length < 1) // no message
        {
            poolingInterval *= 2;

            if (poolingInterval > settings.PoolingMaxInterval)
            {
                poolingInterval = settings.PoolingMaxInterval;
            }

            state = state with
            {
                CurrentPoolingInterval = poolingInterval,
            };

            if (poolingInterval >= settings.PoolingMaxInterval)
            {
                _persistence.State = state;
                await _persistence.WriteStateAsync(cancellationToken);
                return true;
            }
        }
        else
        {
            poolingInterval = settings.PoolingMinInterval;
        }

        if (_timer is not null)
        {
            _timer?.Change(poolingInterval, poolingInterval);
        }
        else
        {
            _timer = _timers.RegisterGrainTimer(
                GrainContext,
                async (grain, cancellationToken) =>
                {
                    var isCancelled = await PullMessages(DateTime.UtcNow, cancellationToken);

                    if (isCancelled)
                    {
                        _timer?.Dispose();
                    }
                },
                this,
                new GrainTimerCreationOptions(poolingInterval, poolingInterval) { KeepAlive = true });
        }

        _persistence.State = state;
        await _persistence.WriteStateAsync(cancellationToken);

        var tasks = new List<Task>();
        foreach (var message in response.Value)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (message.DequeueCount > settings.MaxDequeueCount)
            {
                await _poisonQueue.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
                await _poisonQueue.SendMessageAsync(message.MessageText, cancellationToken: cancellationToken);;
                await _mainQueue.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken: cancellationToken);;
                _logger.LogError("Message '{MessageId}' has been moved to poison queue after {RetryCount} retries.", message.MessageId, message.DequeueCount - 1);
                continue;
            }

            tasks.Add(Task.WhenAny(
                receivers.Select(receiver => ExecuteReceiver(message, receiver))));
        }

        // TODO: Do we want to await?
        // await Task.WhenAll(tasks);
        return false;
    }

    private async Task ExecuteReceiver(QueueMessage message, ActionModel<MessageActionAttribute, QueueMessage> receiverModel)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("crumble.action", ActivityKind.Producer);

        if (activity is not null)
        {
            activity.AddTag("actionType", "message");
            activity.AddTag("queueKey", receiverModel.Action.QueueKey);
            activity.AddTag("messageId", message.MessageId);
        }

        try
        {
            await receiverModel.Executor(_serviceProvider, message);
            Telemetry.Actions.Add(
                1,
                new KeyValuePair<string, object?>("outcome", "success"),
                new KeyValuePair<string, object?>("actionType", "message"),
                new KeyValuePair<string, object?>("queueKey", receiverModel.Action.QueueKey),
                new KeyValuePair<string, object?>("messageId", message.MessageId));

            await _mainQueue.DeleteMessageAsync(message.MessageId, message.PopReceipt);
        }
        catch (Exception)
        {
            Telemetry.Actions.Add(
                1,
                new KeyValuePair<string, object?>("outcome", "failure"),
                new KeyValuePair<string, object?>("actionType", "message"),
                new KeyValuePair<string, object?>("queueKey", receiverModel.Action.QueueKey),
                new KeyValuePair<string, object?>("messageId", message.MessageId));
            throw;
        }
        finally
        {
            Telemetry.Actions.Add(
                1,
                new KeyValuePair<string, object?>("outcome", "engine"),
                new KeyValuePair<string, object?>("actionType", "message"),
                new KeyValuePair<string, object?>("queueKey", receiverModel.Action.QueueKey),
                new KeyValuePair<string, object?>("messageId", message.MessageId));
        }
    }
}
