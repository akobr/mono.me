namespace _42.Testing.UI.Automation;

// Reports one run to a test case management system; one instance per run. The runtime's dispatcher calls it from a
// background consumer, never from a test: StartAsync once, ReportAsync with batches, CompleteAsync once, then
// DisposeAsync. Its exceptions are caught there, logged as run warnings and never reach a test. A failure worth
// retrying (throttling, 5xx, a timeout) is thrown as a TransientReporterException, which the dispatcher retries with
// back-off. TestIdentity.Cases holds only the links that apply to System.
public interface ITestRunReporter : IAsyncDisposable
{
    // The system's name, e.g. "Testiny"; matches the Reporting:<System> section and TestCaseAttribute.System.
    string System { get; }

    // Where the run can be seen, once StartAsync created it; printed at the end of the run and in the CI summary.
    Uri? RunUrl { get; }

    // plannedTests: every test the engine is about to run, so a reporter can put their cases into the run as not run.
    Task StartAsync(TestRunDescriptor run, IReadOnlyList<TestIdentity> plannedTests, CancellationToken cancellationToken);

    Task ReportAsync(IReadOnlyList<TestResultReport> results, CancellationToken cancellationToken);

    Task CompleteAsync(TestRunSummary summary, CancellationToken cancellationToken);
}
