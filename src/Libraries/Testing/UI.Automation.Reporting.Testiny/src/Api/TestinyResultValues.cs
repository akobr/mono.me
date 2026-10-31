using System.Text.Json.Serialization;

namespace _42.Testing.UI.Automation.Reporting.Testiny.Api;

// The values of a test case in a run (TestRunTestCaseValues); only the result status is sent. There is no duration.
public sealed record TestinyResultValues([property: JsonPropertyName("result_status")] string ResultStatus);
