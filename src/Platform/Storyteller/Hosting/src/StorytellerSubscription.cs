using _42.Platform.Storyteller.Sdk.Configuring;
using _42.Platform.Storyteller.Sdk.Configuring.Documents;

namespace _42.Platform.Storyteller.Sdk.Hosting;

// A subscription as the subscriptions hold it: its execution and usage from the informer, and the last good document of
// the execution. Immutable; the subscriptions hand out a new one when something of it changed. ToString() leaves the
// document out, as it may hold resolved secrets.
public sealed class StorytellerSubscription
{
    private readonly DocumentEntry _entry;

    internal StorytellerSubscription(DocumentEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _entry = entry;
    }

    public required StorytellerExecution Execution { get; init; }

    // The usage usg.{subject}.{responsibility}: its labels and values are the subject's entitlement facts.
    public required StorytellerAnnotation Usage { get; init; }

    public string Subject => Execution.SubjectName;

    // A new document per access over the last good read, so what one caller changes in it reaches neither the
    // subscriptions nor another caller.
    public StorytellerDocument Document => new(_entry);

    // The failure of the latest read of the document, while it keeps the last good one; null after a good read.
    public StorytellerConfigurationException? LastError { get; init; }

    public DateTimeOffset? LastErrorAt { get; init; }

    // When a read of the document last succeeded.
    public required DateTimeOffset LastSucceededAt { get; init; }

    // The shared entry, which the subscriptions compare with the next read.
    internal DocumentEntry Entry => _entry;

    public override string ToString()
    {
        return LastError is null
            ? $"{Execution.Key} ({Subject})"
            : $"{Execution.Key} ({Subject}, failing)";
    }
}
