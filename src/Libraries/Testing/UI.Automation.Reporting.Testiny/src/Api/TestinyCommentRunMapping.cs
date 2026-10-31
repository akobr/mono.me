using System.Text.Json.Serialization;

namespace _42.Testing.UI.Automation.Reporting.Testiny.Api;

// One item of POST /comment/mapping/bulk/comment:testcase:testrun: a comment on the result of a test case in a run.
public sealed record TestinyCommentRunMapping([property: JsonPropertyName("ids")] TestinyCommentRunIds Ids);
