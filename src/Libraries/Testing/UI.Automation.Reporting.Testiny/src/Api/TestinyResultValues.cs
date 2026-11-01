using System.Text.Json.Serialization;

namespace _42.Testing.UI.Automation.Reporting.Testiny.Api;

// The values of a test case in a run (TestRunTestCaseValues): the result status and the assignee, which Testiny
// requires (USER, OWNER or ANY); ANY leaves the case to anyone. There is no duration.
public sealed record TestinyResultValues(
    [property: JsonPropertyName("result_status")] string ResultStatus,
    [property: JsonPropertyName("assigned_to")] string AssignedTo = TestinyResultValues.AnyAssignee)
{
    public const string AnyAssignee = "ANY";
}
