using System.Text.Json.Serialization;

namespace _42.Testing.UI.Automation.Reporting.Testiny.Api;

// One item of POST /testrun/mapping/bulk/testcase:testrun: the test case in the run, with its result.
public sealed record TestinyTestCaseRunMapping(
    [property: JsonPropertyName("ids")] TestinyTestCaseRunIds Ids,
    [property: JsonPropertyName("mapped")] TestinyResultValues? Mapped = null);
