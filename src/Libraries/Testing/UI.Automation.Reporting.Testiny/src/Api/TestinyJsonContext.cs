using System.Text.Json.Serialization;

namespace _42.Testing.UI.Automation.Reporting.Testiny.Api;

// The bodies of the Testiny API; the names come from the types' JsonPropertyName attributes (snake_case as in the
// OpenAPI document). Nulls are not written, so a body holds only what is set.
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TestinyTestRun))]
[JsonSerializable(typeof(List<TestinyTestCaseRunMapping>))]
[JsonSerializable(typeof(List<TestinyComment>))]
[JsonSerializable(typeof(List<TestinyCommentRunMapping>))]
[JsonSerializable(typeof(TestinyBlob))]
[JsonSerializable(typeof(TestinyApiError))]
[JsonSerializable(typeof(TestinyFindRequest))]
[JsonSerializable(typeof(TestinyFindResult))]
internal sealed partial class TestinyJsonContext : JsonSerializerContext
{
}
