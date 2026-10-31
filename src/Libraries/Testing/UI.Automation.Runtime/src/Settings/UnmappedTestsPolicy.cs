namespace _42.Testing.UI.Automation.Runtime.Settings;

// Reporting:UnmappedTests, for tests without a [TestCase] for a reporter: nothing, a run warning, or a warning and a
// failed session.
public enum UnmappedTestsPolicy
{
    Ignore,
    Warn,
    Fail,
}
