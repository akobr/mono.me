using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Sdk.Hosting.UnitTests;

// Annotations as the API writes a point read: the type discriminator, the scope, the key and the name, and what a test
// adds.
internal static class TestAnnotations
{
    public const string ErrorJson = """{ "Message": "No.", "Hint": "Ask the owner.", "ErrorCode": "AccessDenied" }""";
    public const string RestrictedJson = """{ "Message": "Restricted.", "ErrorCode": "AnnotationRestricted" }""";

    public static string Json(string annotationKey, Action<JObject>? configure = null)
    {
        var key = AnnotationKey.Parse(annotationKey);
        var json = new JObject
        {
            ["AnnotationType"] = key.Type.ToString(),
            ["ProjectName"] = "billing",
            ["ViewName"] = "default",
            ["AnnotationKey"] = annotationKey,
            ["Name"] = key.Name,
        };

        configure?.Invoke(json);
        return json.ToString();
    }

    public static Action<JObject> Deleted(string at = "2026-10-26T10:00:00Z")
    {
        return json => json["Deletion"] = new JObject
        {
            ["DeletionId"] = "del_1",
            ["RootKey"] = json["AnnotationKey"]!.Value<string>(),
            ["DeletedAt"] = at,
            ["DeletedBy"] = new JObject { ["Kind"] = "Account", ["Id"] = "user_1" },
            ["IsRoot"] = true,
        };
    }
}
