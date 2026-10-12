namespace _42.Platform.Storyteller;

// The body of a create or update.
public record class KeyValueWrite
{
    // Required on create, so a secret never becomes public by omission. On update, unchanged when missing.
    public KeyValueProtection? Protection { get; init; }

    public required string Value { get; init; }

    public string? ContentType { get; init; }

    // null: no check; 0: the key must not exist, or must be deleted; otherwise the current version.
    public ulong? ExpectedVersion { get; init; }
}
