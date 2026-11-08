using System.Text.Json;

namespace _42.Platform.Cli.AutomationTests.Running;

// The JSON options of the tests: sform writes PascalCase (Newtonsoft), so property names match case-insensitively.
public static class JsonDefaults
{
    public static JsonSerializerOptions Web { get; } = new(JsonSerializerDefaults.Web);
}
