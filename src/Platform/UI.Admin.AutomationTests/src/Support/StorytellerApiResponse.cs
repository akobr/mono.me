using System.Text.Json;
using System.Text.Json.Nodes;

namespace _42.Platform.UI.Admin.AutomationTests.Support;

// The answer of the Storyteller API to StorytellerApi.SendAsync: the status and the body as text.
public sealed record StorytellerApiResponse(int Status, string Body)
{
    public bool IsSuccess => Status is >= 200 and < 300;

    // The body as JSON; null for an empty body.
    public JsonNode? Json()
    {
        return string.IsNullOrWhiteSpace(Body) ? null : JsonNode.Parse(Body);
    }

    // The API's message of a refusal (ErrorResponse.Message), else the body; for a log or an exception.
    public string Message()
    {
        try
        {
            return Json()?["Message"]?.GetValue<string>() is { Length: > 0 } message ? message : Body;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return Body;
        }
    }
}
