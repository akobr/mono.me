namespace _42.Testing.UI.Automation;

// The result of one attempt of one test. Attempt is 1-based; a retried test is reported once, with its last attempt.
// Properties: browser, engine, provider, device, the user (a hash of the e-mail), seed; keys in TestRunProperties.
public sealed record TestResultReport(
    TestIdentity Test,
    TestOutcome Outcome,
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    int Attempt,
    TestFailure? Failure,
    string? Output,
    IReadOnlyList<TestArtifact> Artifacts,
    IReadOnlyDictionary<string, string> Properties);
