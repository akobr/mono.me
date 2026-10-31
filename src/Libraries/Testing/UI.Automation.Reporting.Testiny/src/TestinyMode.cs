namespace _42.Testing.UI.Automation.Reporting.Testiny;

// How a run reaches Testiny. TestRun: a classic test run whose test cases are the [TestCase] ids. Automation: the
// automation API (POST /automation, separate automation test cases with native durations); not implemented yet.
public enum TestinyMode
{
    TestRun,
    Automation,
}
