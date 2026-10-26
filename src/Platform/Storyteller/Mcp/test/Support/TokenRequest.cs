using Microsoft.Extensions.Primitives;

namespace _42.Platform.Storyteller.Mcp.UnitTests.Support;

internal sealed record TokenRequest(HttpMethod Method, Uri Uri, string? Authorization, string? ContentType, Dictionary<string, StringValues> Form)
{
    public string Field(string name) => Form.TryGetValue(name, out var value) ? value.ToString() : string.Empty;
}
