using System.Text.Json;
using System.Text.Json.Serialization;

namespace _42.Testing.UI.Automation.Runtime.Reporting;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ResultsFileLine))]
internal sealed partial class ResultsFileJsonContext : JsonSerializerContext
{
}
