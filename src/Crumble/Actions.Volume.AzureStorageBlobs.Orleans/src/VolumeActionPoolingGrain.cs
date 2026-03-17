using System.Diagnostics;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.FileSystemGlobbing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Timers;

namespace _42.Crumble;

[GenerateSerializer]
public record VolumeActionPoolingState(
    [property: Id(0)] DateTime? LastPollingAt,
    [property: Id(1)] TimeSpan? CurrentPoolingInterval,
    [property: Id(2)] int NumberOfFindings = 0);

public class VolumeActionPoolingGrain : IGrainBase, IVolumeActionPoolingGrain, IRemindable
{
    private readonly string _volumeKey;
    private readonly BlobContainerClient _volume;

    private readonly IActionRegistry _registry;
    private readonly IReminderRegistry _reminders;
    private readonly ITimerRegistry _timers;
    private readonly IPersistentState<VolumeActionPoolingState> _persistence;
    private readonly IOptionsMonitor<VolumePoolingSettings> _optionsMonitor;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<VolumeActionPoolingGrain> _logger;

    private IGrainReminder? _reminder;
    private IGrainTimer? _timer;

    public VolumeActionPoolingGrain(
        IActionRegistry registry,
        IReminderRegistry reminders,
        ITimerRegistry timers,
        BlobServiceClient client,
        IPersistentState<VolumeActionPoolingState> persistence,
        IOptionsMonitor<VolumePoolingSettings> optionsMonitor,
        IGrainContext context,
        IServiceProvider serviceProvider,
        ILogger<VolumeActionPoolingGrain> logger)
    {
        _registry = registry;
        _reminders = reminders;
        _timers = timers;
        _persistence = persistence;
        _optionsMonitor = optionsMonitor;
        _serviceProvider = serviceProvider;
        _logger = logger;

        // VolumePooling:VolumeKey
        var grainKey = context.GrainId.ToString();

        if (grainKey.Length < 16 || grainKey[13] != ':')
        {
            throw new ArgumentException("Invalid grain key.", nameof(context.GrainId));
        }

        _volumeKey = grainKey[14..];
        _volume = client.GetBlobContainerClient(_volumeKey);
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
            "VolumePooling",
            TimeSpan.Zero,
            settings.PoolingInterval);
    }

    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        var isCancelled = await PullContent(status.CurrentTickTime, CancellationToken.None);

        if (isCancelled)
        {
            _reminder ??= await _reminders.GetReminder(GrainContext.GrainId, reminderName);
            _reminders.UnregisterReminder(GrainContext.GrainId, _reminder);
            GrainContext.Deactivate(new DeactivationReason(DeactivationReasonCode.ActivationIdle, "There is no receiver."));
            _logger.LogWarning("There is no receiver for content from volume '{VolumeKey}'.", _volumeKey);
        }
    }

    private async Task<bool> PullContent(DateTime currentTickTime, CancellationToken cancellationToken = default)
    {
        var allGroups = _registry
            .GetActions<VolumeActionAttribute, string>()
            .GroupBy(d => d.Action.VolumeKey ?? "default");

        var queueGroup = allGroups.FirstOrDefault(g => g.Key == _volumeKey);

        if (queueGroup is null) // no receiver
        {
            return true;
        }

        var receivers = queueGroup.ToArray();

        if (receivers.Length > 1) // multiple receivers
        {
            _logger.LogInformation("There are multiple receivers for content from volume '{VolumeKey}'.", _volumeKey);
        }

        var numberOfTriggers = 0;
        var tasks = new List<Task>();

        foreach (var receiver in receivers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var normalizedFilter = NormalizeFilter(receiver.Action.PathFilter);
            var literalPrefix = GetLiteralFilterPrefix(normalizedFilter);
            var matcher = new Matcher(StringComparison.Ordinal);
            matcher.AddInclude(normalizedFilter);

            var paging = _volume.GetBlobsAsync(
                new GetBlobsOptions { Prefix = literalPrefix },
                cancellationToken: cancellationToken);

            await foreach (var blob in paging)
            {
                if (!matcher.Match(blob.Name).HasMatches)
                {
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                numberOfTriggers++;
                tasks.Add(ExecuteReceiver(blob.Name, receiver));
            }
        }

        var settings = _optionsMonitor.CurrentValue;
        var newState = _persistence.State with
        {
            LastPollingAt = currentTickTime,
            CurrentPoolingInterval = settings.PoolingInterval,
            NumberOfFindings = numberOfTriggers,
        };

        _persistence.State = newState;
        await _persistence.WriteStateAsync(cancellationToken);

        // TODO: Do we want to await?
        // await Task.WhenAll(tasks);
        return false;
    }

    private async Task ExecuteReceiver(string blobPath, ActionModel<VolumeActionAttribute, string> receiverModel)
    {
        using var activity = Telemetry.ActivitySource.StartActivity("crumble.action", ActivityKind.Producer);

        if (activity is not null)
        {
            activity.AddTag("actionType", "volume");
            activity.AddTag("volumeKey", receiverModel.Action.VolumeKey);
            activity.AddTag("blobPath", blobPath);
        }

        try
        {
            await receiverModel.Executor(_serviceProvider, blobPath);
            Telemetry.Actions.Add(
                1,
                new KeyValuePair<string, object?>("outcome", "success"),
                new KeyValuePair<string, object?>("actionType", "volume"),
                new KeyValuePair<string, object?>("volumeKey", receiverModel.Action.VolumeKey),
                new KeyValuePair<string, object?>("blobPath", blobPath));
        }
        catch (Exception)
        {
            Telemetry.Actions.Add(
                1,
                new KeyValuePair<string, object?>("outcome", "failure"),
                new KeyValuePair<string, object?>("actionType", "volume"),
                new KeyValuePair<string, object?>("volumeKey", receiverModel.Action.VolumeKey),
                new KeyValuePair<string, object?>("blobPath", blobPath));
            throw;
        }
        finally
        {
            Telemetry.Actions.Add(
                1,
                new KeyValuePair<string, object?>("outcome", "engine"),
                new KeyValuePair<string, object?>("actionType", "volume"),
                new KeyValuePair<string, object?>("volumeKey", receiverModel.Action.VolumeKey),
                new KeyValuePair<string, object?>("blobPath", blobPath));
        }
    }

    private static string NormalizeFilter(string pattern)
    {
        return pattern.TrimStart('/').Replace('\\', '/');
    }

    private static string GetLiteralFilterPrefix(string pattern)
    {
        var metas = new HashSet<char>{ '*', '?' };
        var i = 0;

        while (i < pattern.Length && !metas.Contains(pattern[i]))
        {
            i++;
        }

        return pattern[..i];
    }
}
