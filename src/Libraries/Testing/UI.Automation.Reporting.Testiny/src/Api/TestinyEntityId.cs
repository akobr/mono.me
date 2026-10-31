using System.Text.Json.Serialization;

namespace _42.Testing.UI.Automation.Reporting.Testiny.Api;

internal sealed record TestinyEntityId
{
    [JsonPropertyName("id")]
    public long Id { get; init; }
}
