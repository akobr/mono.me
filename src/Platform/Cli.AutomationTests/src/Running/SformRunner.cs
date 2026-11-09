using System.Globalization;
using System.Text;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Running.Processes;

namespace _42.Platform.Cli.AutomationTests.Running;

// Starts sform out of process for one test, the way a script or a CI job runs it:
//   - "dotnet <42.Platform.Cli.dll>" (or the executable of Cli:Path), the arguments as a list, no shell;
//   - in the run's working directory and with SFORM_HOME (both created when missing; SformRunOptions.WithoutHome leaves
//     SFORM_HOME out);
//   - with an environment built from scratch (SformProcessEnvironment): the machine's whitelist, then
//     DOTNET_ENVIRONMENT=Production, no telemetry, NO_COLOR, COLUMNS, then SFORM_TOKEN_STORE=file, then for a user's run
//     SFORM_general__baseUrl=Target:BaseUrl, for a machine its STORYTELLER_* variables, in a terminal TERM, COLUMNS and
//     LINES of its size, and last the run's own variables (null removes);
//   - the standard input gets SformRunOptions.Input and is closed (at once without one), so a prompt fails fast;
//   - a timeout (Cli:TimeoutMs or the run's) kills the whole process tree and every process it started (ProcessJob)
//     and fails with the output so far; a process it left running that holds its streams is killed a few seconds after
//     sform ended (RedirectedChild).
// Each run is attached to the test as sform-<n>.txt (the command line, the names of the variables, never their values,
// the working directory, the exit code, the duration and both streams, WriteJson's folds joined), after the secrets it
// printed were registered (SecretScanner: a machine's key, a client secret, the ::add-mask:: values; and what
// SformRunOptions.SecretsOf returns) and through the scope's redaction.
public sealed class SformRunner
{
    private readonly SformRunnerOptions options;
    private readonly Lazy<SformLocator> locator;
    private int runs;

    public SformRunner(SformRunnerOptions? options = null)
    {
        this.options = options ?? new SformRunnerOptions();
        locator = new Lazy<SformLocator>(() => this.options.Locator ?? SformLocator.Resolve(this.options.CliPath, this.options.FileSystem));
    }

    public SformLocator Locator => locator.Value;

    public SformRunnerOptions Options => options;

    // How many runs started; the next attachment is sform-<RunCount + 1>.txt.
    public int RunCount => Volatile.Read(ref runs);

    // Runs sform to its end; throws an SformTimeoutException (after attaching the run) when it took too long.
    public async Task<SformResult> RunAsync(IReadOnlyList<string> arguments, SformRunOptions? runOptions = null)
    {
        runOptions ??= new SformRunOptions();
        await using var process = await StartAsync(arguments, runOptions);
        if (!process.IsTerminal)
        {
            if (runOptions.Input is { Length: > 0 } input)
            {
                try
                {
                    await process.WriteAsync(input);
                }
                catch (IOException)
                {
                    // sform ended before it read its input; its result says why
                }
            }

            process.CloseInput();
        }

        return await process.WaitForExitAsync();
    }

    // Starts sform and returns while it runs (the device code, a prompt in a terminal). Dispose the process to kill it.
    public Task<SformProcess> StartAsync(IReadOnlyList<string> arguments, SformRunOptions? runOptions = null)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        runOptions ??= new SformRunOptions();

        var fileSystem = options.FileSystem;
        var home = runOptions.Home ?? options.Home ?? throw new InvalidOperationException("A run of sform needs a home (SFORM_HOME): SformRunOptions.Home or SformRunnerOptions.Home.");
        var workingDirectory = runOptions.WorkingDirectory ?? options.WorkingDirectory ?? throw new InvalidOperationException("A run of sform needs a working directory: SformRunOptions.WorkingDirectory or SformRunnerOptions.WorkingDirectory.");
        fileSystem.Directory.CreateDirectory(home.Path);
        fileSystem.Directory.CreateDirectory(workingDirectory);

        if (runOptions.Terminal && TerminalSupport.Reason(fileSystem) is { } reason)
        {
            throw new PlatformNotSupportedException(reason);
        }

        foreach (var secret in runOptions.Secrets.Concat(runOptions.Machine?.Secrets ?? []))
        {
            options.AddSecret?.Invoke(secret);
        }

        var environment = SformProcessEnvironment.Build(home.Path, Layers(runOptions), withoutHome: runOptions.WithoutHome);
        var sform = Locator;
        var request = new StartRequest(
            sform.FileName,
            [.. sform.Prefix, .. arguments],
            workingDirectory,
            environment,
            runOptions.TerminalColumns,
            runOptions.TerminalRows);

        var number = Interlocked.Increment(ref runs);
        var timeout = runOptions.Timeout ?? options.Timeout;
        var names = environment.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var process = new SformProcess(
            sink => Start(request, runOptions.Terminal, sink),
            arguments,
            workingDirectory,
            home.Path,
            timeout,
            Redact,
            (result, timedOut) => CompleteAsync(number, result, timedOut, runOptions, names),
            options.CancellationToken);
        return Task.FromResult(process);
    }

    // The attachment text of a run (unredacted; AttachTextAsync redacts it).
    public static string Describe(SformResult result, bool timedOut, IReadOnlyList<string> environmentNames, string? label)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(environmentNames);

        var builder = new StringBuilder()
            .Append(result.CommandLine).Append('\n');
        if (!string.IsNullOrWhiteSpace(label))
        {
            builder.Append("label: ").Append(label).Append('\n');
        }

        builder
            .Append("working directory: ").Append(result.WorkingDirectory).Append('\n')
            .Append(environmentNames.Contains(SformProcessEnvironment.Home, StringComparer.OrdinalIgnoreCase) ? "SFORM_HOME: " : "SFORM_HOME: not set; HOME of the run: ").Append(result.Home).Append('\n')
            .Append("environment (names): ").Append(string.Join(", ", environmentNames)).Append('\n')
            .Append("terminal: ").Append(result.IsTerminal ? "yes" : "no (standard streams redirected)").Append('\n')
            .Append("exit: ").Append(SformResult.Describe(result.ExitCode))
            .Append(" after ").Append(result.Duration.TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture)).Append(" s")
            .Append(timedOut ? " (timed out: the process tree was killed)" : string.Empty).Append('\n');
        if (!string.Equals(result.PlainStdOut, AnsiText.Plain(result.StdOut), StringComparison.Ordinal))
        {
            builder.Append("note: the JSON values sform's renderer folded at 80 columns are joined (WrappedJson)").Append('\n');
        }

        builder
            .Append('\n')
            .Append(result.IsTerminal ? "--- terminal ---" : "--- stdout ---").Append('\n')
            .Append(result.PlainStdOut).Append('\n');
        if (!result.IsTerminal)
        {
            builder.Append("--- stderr ---").Append('\n').Append(result.PlainStdErr).Append('\n');
        }

        return builder.ToString();
    }

    private ChildProcess Start(StartRequest request, bool terminal, Action<SformStream, string> sink)
    {
        if (!terminal)
        {
            return RedirectedChild.Start(request, sink, options.FileSystem);
        }

        if (OperatingSystem.IsWindows())
        {
            return ConPtyChild.Start(request, sink);
        }

        if (OperatingSystem.IsLinux())
        {
            return ScriptChild.Start(request, sink, options.FileSystem);
        }

        throw new PlatformNotSupportedException(TerminalSupport.Reason(options.FileSystem));
    }

    private string Redact(string? text)
    {
        return options.Redact(text);
    }

    // The variables of the run over the machine's whitelist and the fixed ones, in order.
    private IEnumerable<IReadOnlyDictionary<string, string?>> Layers(SformRunOptions runOptions)
    {
        var runner = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [SformProcessEnvironment.TokenStore] = "file",
        };

        if (runOptions.Machine is null && (runOptions.BaseUrl ?? options.BaseUrl) is { Length: > 0 } baseUrl)
        {
            runner[SformProcessEnvironment.BaseUrl] = baseUrl;
        }

        yield return runner;

        if (runOptions.Machine is { } machine)
        {
            yield return machine.Variables;
        }

        if (runOptions.Terminal)
        {
            yield return new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["TERM"] = "xterm-256color",
                ["COLUMNS"] = runOptions.TerminalColumns.ToString(CultureInfo.InvariantCulture),
                ["LINES"] = runOptions.TerminalRows.ToString(CultureInfo.InvariantCulture),
            };
        }

        yield return runOptions.Environment;
    }

    private async Task CompleteAsync(int number, SformResult result, bool timedOut, SformRunOptions runOptions, IReadOnlyList<string> names)
    {
        // PlainStdOut has WriteJson's folds joined, so a key the renderer split over two lines is found, and masked, whole.
        var secrets = SecretScanner.Find(result.PlainStdOut)
            .Concat(SecretScanner.Find(result.PlainStdErr))
            .Concat(runOptions.SecretsOf?.Invoke(result) ?? []);
        foreach (var secret in secrets)
        {
            if (!string.IsNullOrWhiteSpace(secret))
            {
                options.AddSecret?.Invoke(secret);
            }
        }

        if (options.Attach is { } attach)
        {
            await attach($"sform-{number}.txt", Describe(result, timedOut, names, runOptions.Label ?? runOptions.Machine?.Name));
        }
    }
}
