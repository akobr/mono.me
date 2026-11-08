namespace _42.Platform.Storyteller.Mcp.UnitTests.Support;

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? Body)
{
    // The media type of the body, without its parameters; null without a body.
    public string? ContentType { get; init; }

    // The request headers (not the content headers) as sent, by case-insensitive name, the values of a name joined by ", ".
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
