using Aspire.Hosting.ApplicationModel;

namespace _42.Platform.Storyteller.AspireHosting;

// The Storyteller resource a destination references, read by the callbacks of WithReference and of the key, so the
// order of the calls does not matter. One per destination: a second WithReference replaces it, and removes the
// relationship it drew.
internal sealed record StorytellerReferenceAnnotation(StorytellerResource Resource, ResourceRelationshipAnnotation? Relationship) : IResourceAnnotation;
