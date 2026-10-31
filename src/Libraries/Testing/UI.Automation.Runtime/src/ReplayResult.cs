namespace _42.Testing.UI.Automation.Runtime;

// What AutomationSession.ReplayAsync sent: the summary of the replayed results and the runs the reporters created.
public sealed record ReplayResult(TestRunSummary Summary, IReadOnlyDictionary<string, Uri> RunUrls, int Results);
