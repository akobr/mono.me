using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace _42.tHolistic;

public interface IModelDescriptor
{
    [JsonProperty("$name")]
    [JsonPropertyName("$name")]
    string? Name { get; }

    [JsonProperty("$priority")]
    [JsonPropertyName("$priority")]
    int? Priority { get; }

    [JsonProperty("$dependencies")]
    [JsonPropertyName("$dependencies")]
    string[]? Dependencies { get; }

    [JsonProperty("$labels")]
    [JsonPropertyName("$labels")]
    string[]? Labels { get; }
}
