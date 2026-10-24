using System.Text.Json;

using ModelContextProtocol.Protocol;

using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.UnitTests.Support;

// Arguments and API answers for tests that call tool methods directly.
internal static class ToolFixtures
{
    public const string Organization = "house";
    public const string Project = "main";
    public const string ViewName = "default";

    private static readonly IReadOnlyDictionary<string, IEnumerable<string>> NoHeaders = new Dictionary<string, IEnumerable<string>>();

    public static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    public static Sdk.ApiException NotFound()
    {
        return new Sdk.ApiException("Not found.", 404, string.Empty, NoHeaders, null);
    }

    public static JObject Structured(this CallToolResult result)
    {
        return JObject.Parse(result.StructuredContent!.Value.GetRawText());
    }

    public static Sdk.AnnotationDeletion Deletion(string root)
    {
        return new Sdk.AnnotationDeletion
        {
            DeletionId = "del-1",
            RootKey = root,
            DeletedAt = new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero),
            DeletedBy = new Sdk.AuditActor { Kind = Sdk.AuditActorKind.Account, Id = "user-1", Name = "Ada" },
            IsRoot = true,
        };
    }
}
