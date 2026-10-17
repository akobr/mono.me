using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Transferring;

// One version of a configuration (an annotation's own document), a template or a schema.
public sealed record class ConfigurationDocument
{
    public required ulong Version { get; init; }

    // Who wrote the version, for people: the display name of the actor, or its id without one.
    public required string Author { get; init; }

    // Who wrote the version; its git commit is signed like an annotation's ("account: <id>"). Without it, the
    // commit is signed with Author.
    public AuditActor? Actor { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required JObject Content { get; init; }
}
