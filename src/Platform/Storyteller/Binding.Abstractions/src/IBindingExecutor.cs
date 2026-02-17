using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding;

public interface IBindingExecutor
{
    public ValueTask<bool> TryBinding(JProperty property, bool includeSecrets);
}
