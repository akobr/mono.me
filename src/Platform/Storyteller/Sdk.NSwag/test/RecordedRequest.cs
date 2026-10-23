namespace _42.Platform.Storyteller.Sdk.UnitTests;

internal sealed record RecordedRequest(
    HttpMethod Method,
    Uri Uri,
    string? Authorization,
    IReadOnlyDictionary<string, string> Headers,
    IReadOnlyDictionary<string, string> Form,
    string? Body,
    string? ContentType);
