using System.Text.Json.Serialization;

namespace _42.Testing.UI.Automation.Reporting.Testiny.Api;

internal sealed record TestinyPagination(
    [property: JsonPropertyName("offset")] int Offset,
    [property: JsonPropertyName("limit")] int Limit);
