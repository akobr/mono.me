using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Sdk.Configuring.UnitTests;

internal static class TestDocuments
{
    public const string Endpoint = "https://storyteller.example.com/api";
    public const string Key = "@acme.billing.default.sbt.northwind";
    public const string WeakTag = "W/\"r.AAECAwQFBgcICQoLDA0ODw\"";
    public const string OtherTag = "W/\"r.ZZECAwQFBgcICQoLDA0ODw\"";

    public const string Content = """{ "db": { "host": "db.local", "port": 5432, "renewal": "2026-10-20T12:00:00.1230000+02:00" } }""";

    public const string ErrorJson = """{ "Message": "No.", "Hint": "Ask the owner.", "ErrorCode": "AccessDenied" }""";

    public static string Json(string content = Content, long version = 3)
    {
        return $$"""
            {
              "AnnotationKey": "sbt.northwind",
              "Version": {{version}},
              "Content": {{content}},
              "Modified": { "At": "2026-10-19T08:30:00Z", "By": { "Kind": "Account", "Id": "user_1" } },
              "Hash": "a1b2c3d4"
            }
            """;
    }

    // As the SDK reads a body: date-like strings stay strings.
    public static Configuration Parse(string json)
    {
        return JsonConvert.DeserializeObject<Configuration>(json, new JsonSerializerSettings { DateParseHandling = DateParseHandling.None })!;
    }

    public static Configuration Document(string content = Content, long version = 3)
    {
        return Parse(Json(content, version));
    }
}
