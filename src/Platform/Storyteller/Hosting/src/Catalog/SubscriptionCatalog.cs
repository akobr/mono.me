using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Configuring.Logging;
using _42.Platform.Storyteller.Sdk.Configuring.Settings;
using _42.Platform.Storyteller.Sdk.Hosting.Informer;
using _42.Platform.Storyteller.Sdk.Hosting.Reading;
using Microsoft.Extensions.Logging;

namespace _42.Platform.Storyteller.Sdk.Hosting.Catalog;

// The executions of the view of the options' Key, through point reads of the catalog's transport, and the informer: a
// polling change source per enumeration over the same reader.
internal sealed class SubscriptionCatalog : ISubscriptionCatalog
{
    // The category an application filters on.
    internal const string InformerLogCategory = "_42.Platform.Storyteller.Sdk.Hosting.Informer";

    // Reads of one poll at once: lists, confirmations and snapshots.
    private const int MaxConcurrentReads = 4;

    // The study's pull on an interval; the provider's RefreshInterval is not reused, as a worker may poll its tenants
    // less often than its own document.
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(30);

    private readonly StorytellerSettings _settings;
    private readonly CatalogReader _reader;
    private readonly ILogger _informerLogger;

    // loggerFactory: the container's, used when the options set none.
    public SubscriptionCatalog(StorytellerSettings settings, CatalogReader reader, ILoggerFactory? loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(reader);

        _settings = settings;
        _reader = reader;

        // A factory the container disposed before a late poll must not fail that poll.
        _informerLogger = new SwitchableLoggerFactory(settings.LoggerFactory ?? loggerFactory).CreateLogger(InformerLogCategory);
    }

    // In any status, deleted ones included, so a deleted execution is told from a failure; it returns null as a missing
    // one does.
    public async Task<StorytellerExecution?> GetExecutionAsync(string key, CancellationToken cancellationToken = default)
    {
        var fullKey = CatalogKeys.Parse(key, _settings.Key, nameof(key));

        if (fullKey.Annotation.Type != AnnotationType.Execution)
        {
            throw new ArgumentException(
                $"'{fullKey}' does not name an execution, exe.{{subject}}.{{responsibility}}.{{context}}.",
                nameof(key));
        }

        try
        {
            var annotation = await _reader.ReadAsync(fullKey, includeDeleted: true, CancellationToken.None, cancellationToken).ConfigureAwait(false);
            return annotation.DeletedAt is null ? (StorytellerExecution)annotation : null;
        }
        catch (StorytellerConfigurationException exception) when (CatalogReader.IsMissing(exception))
        {
            return null;
        }
    }

    // The arguments are checked at the call, not at the first MoveNextAsync.
    public IAsyncEnumerable<StorytellerExecutionChange> WatchExecutionsAsync(string responsibility, string context, TimeSpan? pollInterval = null, CancellationToken cancellationToken = default)
    {
        var interval = pollInterval ?? DefaultPollInterval;

        if (interval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(pollInterval), interval, "The poll interval must be zero, for one poll, or positive.");
        }

        var scope = SubscriptionScope.Create(_settings.Key, responsibility, context);
        var source = new PollingSubscriptionSource(_reader, interval, MaxConcurrentReads, _settings.TimeProvider, _settings.NextRandom, _informerLogger);
        return WatchAsync(source, scope, pollsOnce: interval == TimeSpan.Zero, cancellationToken);
    }

    // pollsOnce: the one poll has no next one to try again, so its failure is thrown rather than an empty answer.
    private static async IAsyncEnumerable<StorytellerExecutionChange> WatchAsync(ISubscriptionChangeSource source, SubscriptionScope scope, bool pollsOnce, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var batch in source.ReadAsync(scope, cancellationToken).ConfigureAwait(false))
        {
            if (batch.Failure is { } failure && pollsOnce)
            {
                ExceptionDispatchInfo.Throw(failure);
            }

            foreach (var change in batch.Changes)
            {
                yield return change;
            }
        }
    }
}
