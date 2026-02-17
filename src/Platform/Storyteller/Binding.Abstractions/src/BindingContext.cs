using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding;

public record class BindingContext(JProperty Property, bool IncludeSecrets)
{
    public required string Path { get; init; }

    public string? BindingKey { get; init; }
}
