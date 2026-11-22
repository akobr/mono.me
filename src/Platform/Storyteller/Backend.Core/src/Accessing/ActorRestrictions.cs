namespace _42.Platform.Storyteller.Accessing;

// What an actor of the user token exchange (Auth:TokenExchange:Actors) may not do for a person, whatever the person's
// roles. The list is closed: a new restriction needs a spec, and a setting or a token that names an unknown one is refused.
public static class ActorRestrictions
{
    // No secret values: reveals, secret version reads and diffs, and exports with secrets are refused, and resolved reads
    // are evaluated without secrets. Writing secrets is not affected, because a write returns no value.
    public const string NoSecretReveal = "NoSecretReveal";

    // The token may not change anything. Every request other than GET, HEAD and OPTIONS is refused, except the operations
    // that only read (ReadOnlyOperations of the API). Reads keep their side effects: audit records of reveals and binding
    // reads, and usage metering. It says nothing about secrets, which NoSecretReveal governs.
    public const string ReadOnly = "ReadOnly";

    public static IReadOnlyList<string> All { get; } = [NoSecretReveal, ReadOnly];

    // Ordinal and exact, as settings and tokens carry them.
    public static bool IsKnown(string? restriction)
    {
        return restriction is not null && All.Contains(restriction, StringComparer.Ordinal);
    }
}
