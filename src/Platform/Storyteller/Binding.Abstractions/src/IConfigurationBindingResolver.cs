using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding;

/// <summary>
/// Walks a configuration document and resolves string bindings and object-binding envelopes in place.
/// </summary>
public interface IConfigurationBindingResolver
{
    ValueTask ResolveAsync(JObject content, bool includeSecrets, BindingScope scope);

    /// <summary>
    /// Whether <see cref="ResolveAsync"/> could change the document. False means the resolved document is the stored one,
    /// so a read can skip the resolver. A resolver that cannot tell returns true and is always run.
    /// </summary>
    bool ContainsBindings(JToken content) => true;
}
