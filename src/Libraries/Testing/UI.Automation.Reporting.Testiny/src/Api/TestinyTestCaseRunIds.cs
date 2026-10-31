using System.Text.Json.Serialization;

namespace _42.Testing.UI.Automation.Reporting.Testiny.Api;

public sealed record TestinyTestCaseRunIds(
    [property: JsonPropertyName("testcase_id")] long TestCaseId,
    [property: JsonPropertyName("testrun_id")] long TestRunId);
