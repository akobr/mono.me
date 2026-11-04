using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Configuring.Documents;

namespace _42.Platform.Storyteller.Sdk.Hosting;

// A subscription as the subscriptions hold it: its execution and usage from the informer, and the last good document of
// the execution. Immutable; the subscriptions hand out a new one when something of it changed. Only the subscriptions
// build one, as only they hold documents (StorytellerDocument has no public constructor either). ToString() leaves the
// document out, as it may hold resolved secrets.
public sealed class StorytellerSubscription
{
    private readonly DocumentEntry _entry;

    internal StorytellerSubscription(
        DocumentEntry entry,
        StorytellerExecution execution,
        StorytellerAnnotation usage,
        DateTimeOffset lastSucceededAt,
        StorytellerConfigurationException? lastError,
        DateTimeOffset? lastErrorAt)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentNullException.ThrowIfNull(usage);

        _entry = entry;
        Execution = execution;
        Usage = usage;
        LastSucceededAt = lastSucceededAt;
        LastError = lastError;
        LastErrorAt = lastErrorAt;
    }

    public StorytellerExecution Execution { get; }

    // The usage usg.{subject}.{responsibility}: its labels and values are the subject's entitlement facts.
    public StorytellerAnnotation Usage { get; }

    public string Subject => Execution.SubjectName;

    // A new document per access over the last good read, so what one caller changes in it reaches neither the
    // subscriptions nor another caller.
    public StorytellerDocument Document => new(_entry);

    // The failure of the latest read of the document, while it keeps the last good one; null after a good read.
    public StorytellerConfigurationException? LastError { get; }

    public DateTimeOffset? LastErrorAt { get; }

    // When a read of the document last succeeded.
    public DateTimeOffset LastSucceededAt { get; }

    // The shared entry, which the subscriptions compare with the next read.
    internal DocumentEntry Entry => _entry;

    public override string ToString()
    {
        return LastError is null
            ? $"{Execution.Key} ({Subject})"
            : $"{Execution.Key} ({Subject}, failing)";
    }
}
