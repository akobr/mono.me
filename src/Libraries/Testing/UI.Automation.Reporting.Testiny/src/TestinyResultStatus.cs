namespace _42.Testing.UI.Automation.Reporting.Testiny;

// The built-in result statuses of a test case in a test run; a project may define more (custom result states).
public static class TestinyResultStatus
{
    public const string NotRun = "NOTRUN";
    public const string Passed = "PASSED";
    public const string Failed = "FAILED";
    public const string Blocked = "BLOCKED";
    public const string Skipped = "SKIPPED";
}
