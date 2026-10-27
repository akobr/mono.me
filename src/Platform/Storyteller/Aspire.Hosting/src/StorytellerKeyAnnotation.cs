using Aspire.Hosting.ApplicationModel;

namespace _42.Platform.Storyteller.AspireHosting;

// The key of STORYTELLER_KEY as WithStorytellerKey took it: a full key, or an annotation key that the callback prefixes
// with the view of the referenced resource. One per destination, so the last call wins.
internal sealed record StorytellerKeyAnnotation(string Key) : IResourceAnnotation;
