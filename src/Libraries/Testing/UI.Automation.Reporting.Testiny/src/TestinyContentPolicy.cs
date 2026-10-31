namespace _42.Testing.UI.Automation.Reporting.Testiny;

// Which results get attachments or a comment. Failures: every outcome but Passed and Skipped (Failed, Timeout,
// Cancelled, Blocked).
public enum TestinyContentPolicy
{
    None,
    Failures,
    All,
}
