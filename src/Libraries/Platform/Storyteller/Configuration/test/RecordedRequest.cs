namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests;

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization, IReadOnlyDictionary<string, string> Headers);
