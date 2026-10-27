namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests;

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, IReadOnlyDictionary<string, string> Headers);
