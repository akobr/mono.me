using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding;

public sealed class BindingValue
{
    public BindingValue(JToken token)
    {
        Token = token ?? throw new ArgumentNullException(nameof(token));
    }

    public JToken Token { get; }

    public static BindingValue FromString(string value)
    {
        return new BindingValue(new JValue(value));
    }
}
