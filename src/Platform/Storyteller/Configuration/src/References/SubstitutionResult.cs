using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Configuring.References;

// The document after substitution. OldestResolution is when its oldest resolved reference was resolved; null without
// references.
internal sealed record SubstitutionResult(JObject Content, DateTimeOffset? OldestResolution);
