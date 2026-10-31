using System.Globalization;
using System.Text;

namespace _42.Testing.UI.Automation.Reporting.Testiny;

// The run's description: the descriptor's description (the runtime's table of the environment, or Reporting:
// RunDescription) under the markdown header; at the end of the run also the totals, the test cases by status and the
// warnings. Comments go to results only, so the description is where the run itself is summed up.
internal static class TestinyRunDescription
{
    public const int MaxWarnings = 50;

    private const int MaxWarningLength = 1_000;

    public static string Started(TestRunDescriptor run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return TestinyMarkdown.Document($"{Body(run)}\n\nRun {TestinyMarkdown.Code(run.RunId)} started {TestinyMarkdown.Time(run.StartedAt)}; results are being reported.");
    }

    // casesByStatus: the status last sent for each case of the run, NOTRUN for the planned cases without a result.
    public static string Completed(
        TestRunDescriptor run,
        TestRunSummary summary,
        IReadOnlyDictionary<string, int> casesByStatus,
        int attachments,
        int comments,
        IReadOnlyList<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(casesByStatus);
        ArgumentNullException.ThrowIfNull(warnings);

        var builder = new StringBuilder(Body(run)).Append("\n\n### Results\n\n")
            .Append("| Passed | Failed | Skipped | Blocked | Total |\n|---|---|---|---|---|\n")
            .Append(CultureInfo.InvariantCulture, $"| {summary.Passed} | {summary.Failed} | {summary.Skipped} | {summary.Blocked} | {summary.Total} |\n\n");

        builder.Append(CultureInfo.InvariantCulture, $"Run {TestinyMarkdown.Code(run.RunId)}: started {TestinyMarkdown.Time(run.StartedAt)}, finished {TestinyMarkdown.Time(summary.FinishedAt)}")
            .Append(CultureInfo.InvariantCulture, $" ({TestinyMarkdown.Duration(summary.FinishedAt - run.StartedAt)}). ")
            .Append(CultureInfo.InvariantCulture, $"Test cases in the run: {Cases(casesByStatus)}; ")
            .Append(CultureInfo.InvariantCulture, $"{attachments} attachment(s), {comments} comment(s).");

        AppendWarnings(builder, warnings);
        return TestinyMarkdown.Document(builder.ToString().TrimEnd('\n'));
    }

    // A run that was created but not completed: its start failed after the creation, so nothing more was reported to
    // it, or its completion failed. reason: why, e.g. "its start failed: …".
    public static string Stopped(TestRunDescriptor run, string reason, IReadOnlyDictionary<string, int> casesByStatus, IReadOnlyList<string> warnings)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(casesByStatus);
        ArgumentNullException.ThrowIfNull(warnings);

        var builder = new StringBuilder(Body(run)).Append("\n\n")
            .Append(CultureInfo.InvariantCulture, $"Run {TestinyMarkdown.Code(run.RunId)} started {TestinyMarkdown.Time(run.StartedAt)}; ")
            .Append(TestinyMarkdown.Text($"its reporting to Testiny stopped before the end, because {TestinyMarkdown.Truncate(reason, MaxWarningLength)}."))
            .Append(CultureInfo.InvariantCulture, $" Test cases in the run: {Cases(casesByStatus)}. ")
            .Append("The results of the run are in its results.jsonl; Reporting:ReplayFile sends them to a new run.");

        AppendWarnings(builder, warnings);
        return TestinyMarkdown.Document(builder.ToString().TrimEnd('\n'));
    }

    // "2 (1 FAILED, 1 NOTRUN)".
    private static string Cases(IReadOnlyDictionary<string, int> casesByStatus)
    {
        var statuses = string.Join(", ", casesByStatus
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Value} {TestinyMarkdown.Text(pair.Key)}")));
        var count = casesByStatus.Values.Sum().ToString(CultureInfo.InvariantCulture);
        return statuses.Length > 0 ? $"{count} ({statuses})" : count;
    }

    private static void AppendWarnings(StringBuilder builder, IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
        {
            return;
        }

        builder.Append(CultureInfo.InvariantCulture, $"\n\n### Warnings ({warnings.Count})\n\n");
        foreach (var warning in warnings.Take(MaxWarnings))
        {
            builder.Append("- ").Append(TestinyMarkdown.Text(TestinyMarkdown.Truncate(warning, MaxWarningLength))).Append('\n');
        }

        if (warnings.Count > MaxWarnings)
        {
            builder.Append(CultureInfo.InvariantCulture, $"- and {warnings.Count - MaxWarnings} more\n");
        }
    }

    private static string Body(TestRunDescriptor run)
    {
        return string.IsNullOrWhiteSpace(run.Description) ? TestinyMarkdown.Text(run.Name) : run.Description.ReplaceLineEndings("\n").TrimEnd('\n');
    }
}
