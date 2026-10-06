using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding;

/// <summary>
/// Walks a configuration document and resolves string bindings and object-binding envelopes in place.
/// </summary>
public interface IConfigurationBindingResolver
{
    ValueTask ResolveAsync(JObject content, bool includeSecrets, BindingScope scope);
}
