using System.Text.Json.Serialization;

namespace _42.Testing.UI.Automation.Reporting.Testiny.Api;

internal sealed record TestinyTestRunFilter(
    [property: JsonPropertyName("project_id")] long ProjectId,
    [property: JsonPropertyName("title")] string Title);
