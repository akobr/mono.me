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

    // The answer of a restore of a version: FromVersion is the version asked for, Version the new or the current one.
    public static Sdk.VersionRestoreResult Restore(Sdk.VersionRestoreResultOutcome outcome, long fromVersion, long version)
    {
        return new Sdk.VersionRestoreResult
        {
            Outcome = outcome,
            FromVersion = fromVersion,
            Version = version,
            Modified = new Sdk.AuditStamp
            {
                At = new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero),
                By = new Sdk.AuditActor { Kind = Sdk.AuditActorKind.Account, Id = "user-1", Name = "Ada" },
            },
        };
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
