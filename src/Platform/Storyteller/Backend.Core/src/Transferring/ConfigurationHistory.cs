namespace _42.Platform.Storyteller.Transferring;

// The own documents of one configuration that an export contains.
public sealed record class ConfigurationHistory
{
    public required string AnnotationKey { get; init; }

    // Oldest first; the current document is the last one.
    public required IReadOnlyList<ConfigurationDocument> Versions { get; init; }

    // Null when the configuration was cleared and only earlier versions remain.
    public ConfigurationDocument? Current { get; init; }
}
