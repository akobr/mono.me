using System.Text;
using System.Text.Json;

namespace _42.Testing.UI.Automation.Reporting.Testiny.UnitTests.Fakes;

// One request FakeTestiny received. Path is below the API root, e.g. "testrun/mapping/bulk/testcase:testrun".
internal sealed record RecordedRequest(
    string Method,
    string Path,
    IReadOnlyDictionary<string, string> Query,
    IReadOnlyDictionary<string, string> Headers,
    string? ContentType,
    byte[] Body)
{
    public string Text => Encoding.UTF8.GetString(Body);

    public JsonElement Json
    {
        get
        {
            using var document = JsonDocument.Parse(Body);
            return document.RootElement.Clone();
        }
    }

    public override string ToString()
    {
        return $"{Method} {Path}";
    }
}
