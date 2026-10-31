using System.Text.Json.Serialization;

namespace _42.Testing.UI.Automation.Reporting.Testiny.Api;

public sealed record TestinyCommentRunIds(
    [property: JsonPropertyName("comment_id")] long CommentId,
    [property: JsonPropertyName("testcase_id")] long TestCaseId,
    [property: JsonPropertyName("testrun_id")] long TestRunId);
