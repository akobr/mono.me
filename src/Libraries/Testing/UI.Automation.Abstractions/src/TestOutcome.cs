namespace _42.Testing.UI.Automation;

// Blocked: the test could not run for a reason outside the system under test, e.g. no free user in the pool or no
// browser. A summary counts Timeout and Cancelled as failures.
public enum TestOutcome
{
    Passed,
    Failed,
    Skipped,
    Blocked,
    Timeout,
    Cancelled,
}
