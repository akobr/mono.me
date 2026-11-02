using System.Text.Json.Nodes;

namespace _42.Platform.UI.Admin.AutomationTests.Support;

// The answer of the mock API to MockApi.FetchAsync: the status and the body as text.
public sealed record MockApiResponse(int Status, string Body)
{
    public bool IsSuccess => Status is >= 200 and < 300;

    // The body as JSON; null for an empty body.
    public JsonNode? Json()
    {
        return string.IsNullOrWhiteSpace(Body) ? null : JsonNode.Parse(Body);
    }
}
