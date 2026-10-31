namespace _42.Testing.UI.Automation.Runtime.Settings;

// The Execution section: parallelism, data seed, timeouts, retries and the artifacts kept per test.
public sealed class ExecutionSettings
{
    // Handed to the engine at discovery; its command line and environment variable win. Lowered to the pool size when
    // the pool is smaller.
    public int MaxParallelTests { get; set; } = 4;

    // The session seed of the generated data; random (and logged) when unset, so a run's data can be reproduced.
    public int? Seed { get; set; }

    // The default timeout of a Playwright action of the test's context.
    public int ActionTimeoutMs { get; set; } = 10_000;

    public int NavigationTimeoutMs { get; set; } = 30_000;

    // The default timeout of Playwright's Expect assertions; set once per session (it is process-wide).
    public int ExpectTimeoutMs { get; set; } = 10_000;

    // How many times a failed test runs again; applied by the engine adapter. Skips and data conflicts are not retried.
    public int Retries { get; set; }

    public ArtifactPolicy Trace { get; set; } = ArtifactPolicy.OnFailure;

    public ArtifactPolicy Screenshot { get; set; } = ArtifactPolicy.OnFailure;

    public ArtifactPolicy Video { get; set; } = ArtifactPolicy.Off;

    // Relative to the directory of the test assembly; the run writes to <ArtifactsPath>/<RunId>/.
    public string ArtifactsPath { get; set; } = "TestResults/artifacts";
}
