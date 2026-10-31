using System.Text.Json.Serialization;

namespace _42.Testing.UI.Automation.Reporting.Testiny.Api;

internal sealed record TestinyProjectFilter([property: JsonPropertyName("project_id")] long ProjectId);
