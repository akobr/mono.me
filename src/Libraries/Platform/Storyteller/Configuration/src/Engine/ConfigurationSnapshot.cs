using _42.Platform.Storyteller.Sdk.Configuring.Reading;
using _42.Platform.Storyteller.Sdk.Configuring.References;

namespace _42.Platform.Storyteller.Sdk.Configuring.Engine;

// What the providers of one engine publish. Immutable and swapped in with one reference assignment; the engine gives
// it its sequence number at the swap, so a provider can tell a newer snapshot from an older one. Compared by reference.
internal sealed record ConfigurationSnapshot
{
    private static readonly IReadOnlyDictionary<string, string> NoData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // Grows with every snapshot the engine swaps in, a rollback included; 0 before the first load.
    public long Sequence { get; init; }

    public required DocumentIdentity Identity { get; init; }

    // The tag the next read of the identity sends. None after a start from a plain cache entry or without a document;
    // the rejected tag after a rollback of the same identity, so an unchanged document answers 304.
    public string? ETag { get; init; }

    // The server's document, before local resolution; null for an empty start.
    public Configuration? Document { get; init; }

    // The content after substitution, with the time of its oldest resolution; null without a document.
    public SubstitutionResult? Substitution { get; init; }

    // Shared by the snapshots that changed only their metadata, so a provider tells that nothing is to reload.
    public required IReadOnlyDictionary<string, string> Data { get; init; }

    public string? CacheControl { get; init; }

    public StorytellerConfigurationOrigin Origin { get; init; }

    // Set on a rollback of the same identity: the platform's current document did not bind.
    public StorytellerConfigurationException? Rejection { get; init; }

    // Set on a rollback of a secret rotation: the data with the values resolved again, which did not bind. A 304 whose
    // resolution gives the same data applies nothing, so an unchanged secret is not applied and rolled back every poll;
    // a value rotated again is tried. Compared, never published or logged.
    public IReadOnlyDictionary<string, string>? RejectedData { get; init; }

    // A 200 of the platform, whose cache entry is written once every provider took it. A rollback never writes: its
    // content is the confirmed one, under the rejected tag.
    public bool WritesCache { get; init; }

    // The outcome of the notification of this very snapshot. The engine gives every snapshot it swaps in a new one, so a
    // copy made with `with` never shares it.
    public NotificationOutcome Notification { get; init; } = new();

    public static ConfigurationSnapshot Empty(DocumentIdentity identity)
    {
        return new ConfigurationSnapshot
        {
            Identity = identity,
            Data = NoData,
            Origin = StorytellerConfigurationOrigin.Empty,
        };
    }

    // The data may hold secrets; a snapshot that ends up in a log names its key only.
    public override string ToString()
    {
        return $"{Identity.Key} #{Sequence} ({Origin})";
    }

    // Guarded by the engine's lock.
    internal sealed class NotificationOutcome
    {
        // A reload threw while this snapshot was notified, or it carried the rejected document of one that did and was
        // rolled back with it.
        public StorytellerConfigurationException? Rejection { get; set; }
    }
}
