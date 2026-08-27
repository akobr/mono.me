using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding;

public interface IBindingExecutor
{
    ValueTask<bool> TryBinding(JProperty property, bool includeSecrets);

    ValueTask<bool> TryBinding(JValue value, bool includeSecrets);
}
