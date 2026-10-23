using _42.Platform.Storyteller.Binding;

using Microsoft.Net.Http.Headers;

namespace _42.Platform.Storyteller.Api.ConditionalReads;

// The inputs of one resolved read with bindings: Tag is its d tag, a digest of the other three.
public sealed record DependencyManifest(
    EntityTagHeaderValue Tag,
    EntityTagHeaderValue Effective,
    string Runtime,
    IReadOnlyList<BindingDependency> Dependencies);
