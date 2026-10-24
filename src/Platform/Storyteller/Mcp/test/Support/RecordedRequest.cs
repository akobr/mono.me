namespace _42.Platform.Storyteller.Mcp.UnitTests.Support;

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, string? Body);
