using _42.Platform.Cli.AutomationTests.Access;

namespace _42.Platform.Cli.AutomationTests.Running;

// How one run of sform is started. Every property is optional; the test base class (CliTest) fills the home, the working
// directory and the base URL, and a test adds what its case needs, e.g.
//   await Sform(new SformRunOptions { Input = "v3\n" }, InProject("story", "kv", "set", key, "--stdin", "--protection", "public"));
//   await Sform(AsMachine("apikey"), "account");
//   await Sform(new SformRunOptions { Environment = { ["GITHUB_ENV"] = file } }, …);
public sealed record SformRunOptions
{
    // Variables to add to (or override in) the environment sform starts with; a null value removes a variable the
    // runner would set (e.g. SFORM_TOKEN_STORE, to check the protected store), except SFORM_HOME (see WithoutHome). The
    // environment is built from scratch (SformProcessEnvironment), so nothing of the machine that runs the tests leaks in
    // unless named here.
    public Dictionary<string, string?> Environment { get; init; } = new(StringComparer.Ordinal);

    // Written to the standard input, which is then closed; null closes it at once, so a prompt fails fast instead of
    // waiting. Ignored in a terminal (answer the prompts with SformProcess.SendAsync and SendKeyAsync).
    public string? Input { get; init; }

    // How long the run may take before its process tree is killed and the run fails with the output so far; null:
    // Cli:TimeoutMs.
    public TimeSpan? Timeout { get; init; }

    // The current directory of sform; null: the test's working directory (CliTest.WorkingDirectory).
    public string? WorkingDirectory { get; init; }

    // SFORM_HOME (unless WithoutHome); null: the leased user's signed-in home, a fresh one with [FreshSignIn], an empty
    // per-test one for a machine. CliTest.FreshHome() makes another empty one.
    public SformHome? Home { get; init; }

    // Sign in as this machine (its STORYTELLER_* variables) instead of the user; CliTest.AsMachine("<name>") builds the
    // options of a configured one. The home is then an empty per-test folder unless Home is given.
    public MachineCredentials? Machine { get; init; }

    // Run sform in a pseudo terminal (ConPTY on Windows, util-linux script on Linux), for the cases that answer a prompt
    // (mode T): stdout and stderr are one screen, the input is the keyboard. Not supported on macOS (the test skips).
    public bool Terminal { get; init; }

    // The size of the pseudo terminal, columns and rows.
    public int TerminalColumns { get; init; } = 250;

    public int TerminalRows { get; init; } = 50;

    // SFORM_general__baseUrl for a user run; null: Target:BaseUrl. A machine's API comes from its endpoint instead.
    public string? BaseUrl { get; init; }

    // Values to mask in the run's attachment and in the messages of its assertions, besides the settings' secrets and
    // what the test registered (CliTest.AddSecret); e.g. a secret value the command line carries.
    public IReadOnlyList<string> Secrets { get; init; } = [];

    // The secrets the test learns only from this run's output (a value of a Key Vault reference that config -r resolved,
    // kv get --reveal of a pair the test did not set): called with the result before the run is attached, so its
    // sform-<n>.txt already masks them. The runner finds sform's own printed credentials and the ::add-mask:: values
    // itself (SecretScanner). It must not throw.
    public Func<SformResult, IEnumerable<string>>? SecretsOf { get; init; }

    // Run without SFORM_HOME (GEN-CLI-06: SFORM_TOKEN_STORE=file is ignored without it). sform then keeps its state next
    // to its build (the folder of 42.Platform.Cli.dll) and its session in the user's folder: on Linux and macOS the run's
    // HOME and XDG_* folders point at its own empty home, so the real profile is not touched; on Windows sform finds the
    // user's folder through the known-folder API, which no variable redirects, so the run reads the real profile. Start
    // such a run (StartSform) and kill it after the line it checks, as the catalogue says.
    public bool WithoutHome { get; init; }

    // A run that ended with 44 and the CLI's general refusal (a session the API no longer accepts) signs the user in
    // again once and runs once more; false keeps the first result. Only for runs in a pool user's own home.
    public bool RenewExpiredSession { get; init; } = true;

    // A short note in the attachment, e.g. "as the Reader".
    public string? Label { get; init; }
}
