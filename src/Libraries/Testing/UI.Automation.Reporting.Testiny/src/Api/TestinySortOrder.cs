using System.Text.Json.Serialization;

namespace _42.Testing.UI.Automation.Reporting.Testiny.Api;

internal sealed record TestinySortOrder(
    [property: JsonPropertyName("column")] string Column,
    [property: JsonPropertyName("order")] string Order);
