using System.Collections.ObjectModel;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Configuring.References;

// The document after substitution. OldestResolution is when its oldest resolved reference was resolved; null without
// references. Content and ResolvedValues may hold secrets, so ToString() leaves them out.
internal sealed record SubstitutionResult(JObject Content, DateTimeOffset? OldestResolution)
{
    // The values with the shape of a SecretReference that stay as written: no resolver is set, or it declined them.
    public IReadOnlyList<UnresolvedReference> Unresolved { get; init; } = [];

    // Whether a resolver saw the document; without one, every reference stays as written.
    public bool HasResolver { get; init; }

    // The resolved values of the document by value text, for a later substitution of the same document to reuse.
    public IReadOnlyDictionary<string, ResolvedValue> ResolvedValues { get; init; } = ReadOnlyDictionary<string, ResolvedValue>.Empty;

    public override string ToString()
    {
        return $"{nameof(SubstitutionResult)} (oldest resolution {OldestResolution?.ToString("O") ?? "none"}, {Unresolved.Count} unresolved)";
    }
}
