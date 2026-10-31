using System.Text.Json.Serialization;

namespace _42.Testing.UI.Automation.Reporting.Testiny.Api;

// A comment (Comment): Type TEXT with markdown Text, whose first line is TestinyMarkdown.Header.
public sealed record TestinyComment
{
    public const string TextType = "TEXT";

    [JsonPropertyName("id")]
    public long? Id { get; init; }

    [JsonPropertyName("project_id")]
    public long? ProjectId { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }
}
