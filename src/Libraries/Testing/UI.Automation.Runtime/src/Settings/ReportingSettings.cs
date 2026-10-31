namespace _42.Testing.UI.Automation.Runtime.Settings;

// The Reporting section: whether and how results go to a test case management system. Each system reads its own
// Reporting:<System> section (e.g. Reporting:Testiny), which the runtime hands to the system's provider.
public sealed class ReportingSettings
{
    public const string DefaultRunName = "UI automation {Date:yyyy-MM-dd} {Time:HH:mm} {Profile}";

    // The master switch: false creates no reporter and leaves [TestCase] inert; results.jsonl is written anyway.
    public bool Enabled { get; set; }

    // The systems to report to; empty means every registered provider whose Reporting:<System> section exists.
    public List<string> Systems { get; set; } = [];

    // A pattern with {RunId}, {Date:format}, {Time:format}, {Profile}, {Provider}, {Engine}, {Device}, {Branch},
    // {Sha7}, {BuildNumber} and {Machine}; {{ and }} are literal braces.
    public string RunName { get; set; } = DefaultRunName;

    // Markdown with the same placeholders; a table of the run's environment when unset.
    public string? RunDescription { get; set; }

    public UnmappedTestsPolicy UnmappedTests { get; set; } = UnmappedTestsPolicy.Ignore;

    // Results are sent in batches of this size, or of what arrived within FlushIntervalSeconds.
    public int BatchSize { get; set; } = 20;

    public int FlushIntervalSeconds { get; set; } = 5;

    // Attempts of a reporter call that failed transiently (throttling, 5xx, timeouts), with exponential back-off.
    public int MaxAttempts { get; set; } = 5;

    // How long the end of the run waits for the reporters to drain and complete.
    public int CompleteTimeoutSeconds { get; set; } = 120;

    // A results.jsonl of an earlier run: the engine adapter sends it through the reporters instead of running tests.
    public string? ReplayFile { get; set; }
}
