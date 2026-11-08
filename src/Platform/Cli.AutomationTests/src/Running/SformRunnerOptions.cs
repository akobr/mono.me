using System.IO.Abstractions;
using _42.Platform.Cli.AutomationTests.Access;

namespace _42.Platform.Cli.AutomationTests.Running;

// What an SformRunner needs from its test: where sform is, how long a run may take, the target's API, where the runs
// happen, and how their attachments are written and redacted. CliTest builds it from the settings and the test's
// scope; a self test without a target builds one with the defaults (no attachments).
public sealed class SformRunnerOptions
{
    // Which sform runs; null: SformLocator.Resolve(CliPath).
    public SformLocator? Locator { get; init; }

    // Cli:Path.
    public string? CliPath { get; init; }

    // Cli:TimeoutMs: a run that takes longer is killed with its process tree.
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMilliseconds(CliSettings.DefaultTimeoutMs);

    // SFORM_general__baseUrl of a user run, Target:BaseUrl; null sets none (sform's own app.config.json applies).
    public string? BaseUrl { get; init; }

    // The home of a run whose options name none.
    public SformHome? Home { get; init; }

    // The working directory of a run whose options name none; created when missing.
    public string? WorkingDirectory { get; init; }

    // Masks the known secrets in a text (TestScope.Redact).
    public Func<string?, string> Redact { get; init; } = static text => text ?? string.Empty;

    // Registers a secret the run printed (TestScope.AddSecret).
    public Action<string>? AddSecret { get; init; }

    // Writes a text attachment of the test (TestScope.AttachTextAsync, which redacts it); null attaches nothing.
    public Func<string, string, Task>? Attach { get; init; }

    // The test's cancellation (TUnit's), linked into every run.
    public CancellationToken CancellationToken { get; init; }

    public IFileSystem FileSystem { get; init; } = new FileSystem();
}
