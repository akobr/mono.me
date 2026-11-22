using System.Collections.Frozen;

using _42.Platform.Storyteller.Api.V1;

namespace _42.Platform.Storyteller.Api.Security;

// The functions that take a method other than GET, HEAD or OPTIONS but only read, so the restriction ReadOnly lets them
// pass (ActorRestrictionMiddleware): a POST that carries a list of keys, or keeps everything secret out of the URL.
// Function names, which are their operation ids too. Only functions that exist and only read; a test walks every
// function, so a new write route is refused under ReadOnly by default.
internal static class ReadOnlyOperations
{
    public static readonly FrozenSet<string> All = FrozenSet.ToFrozenSet(
    [
        // The deleted annotations among the keys a client is about to create.
        nameof(AnnotationsHttp.FindDeletedAnnotations),

        // The pairs of a view as a document; its secret values stay governed by NoSecretReveal.
        nameof(KeyValuesHttp.ExportKeyValues),
    ],
    StringComparer.Ordinal);
}
