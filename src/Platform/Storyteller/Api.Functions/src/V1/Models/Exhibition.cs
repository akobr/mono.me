using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Api.V1.Models;

// The exhibition of the deployment: its demo project, which a user may join as a Reader. The exhibition is the view
// default of the project. The OpenAPI document takes required from Newtonsoft, so the generated types have both names.
public record class Exhibition
{
    [JsonProperty(Required = Required.Always)]
    public required string Organization { get; init; }

    [JsonProperty(Required = Required.Always)]
    public required string Project { get; init; }

    // The description of the project, when it has one.
    public string? Description { get; init; }
}
