using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using _42.Platform.Cli.AutomationTests.Running.Processes;
using _42.Platform.Cli.AutomationTests.Running.Terminal;

namespace _42.Platform.Cli.AutomationTests.Running;

// A running sform that the test talks to while it runs: the device code of a sign-in (wait for the JSON line on the
// standard error, approve it in a browser, then wait for the end), or a prompt in a pseudo terminal (expect its text,
// send the answer). Started by CliTest.StartSform (SformRunner.StartAsync); in a terminal with SformRunOptions.Terminal
// (CliTest.StartSformInTerminal).
//
//   await using var account = await StartSform(new SformRunOptions { Environment = { ["SFORM_DEVICE_CODE_OUTPUT"] = "json" } }, "account");
//   var line = await account.WaitForLineAsync(SformStream.StdErr, text => text.StartsWith('{'));
//   …
//   var result = await account.WaitForExitAsync();
//
//   await using var terminal = await StartSformInTerminal("assist", "key");
//   await terminal.ExpectAsync("What type of annotation you need");
//   await terminal.SendKeyAsync(TerminalKey.Down);
//   await terminal.SendKeyAsync(TerminalKey.Enter);
//   await terminal.ExpectAsync("Name of the responsibility");
//   await terminal.SendLineAsync("my-app");
//   (await terminal.WaitForExitAsync()).ShouldSucceed();
//
// The whole run has the timeout of its options (Cli:TimeoutMs): when it passes, the process tree is killed, a wait
// fails, and WaitForExitAsync throws an SformTimeoutException with the output so far. Each wait has its own timeout
// too (30 s by default). The run is attached to the test (sform-<n>.txt) once it ended, also when the test disposes it
// running (it is killed then). Every text a wait reports in its message is redacted.
public sealed class SformProcess : IAsyncDisposable
{
    public static readonly TimeSpan DefaultWaitTimeout = TimeSpan.FromSeconds(30);

    private readonly ChildProcess child;
    private readonly object gate = new();
    private readonly StringBuilder stdOut = new();
    private readonly StringBuilder stdErr = new();
    private readonly StringBuilder screen = new();
    private readonly StringBuilder pendingOut = new();
    private readonly StringBuilder pendingErr = new();
    private readonly List<SformLine> timeline = [];
    private readonly Stopwatch stopwatch;
    private readonly Func<string?, string> redact;
    private readonly Func<SformResult, bool, Task> completed;
    private readonly CancellationTokenSource lifetime;
    private readonly Task<SformResult> finished;
    private TaskCompletionSource changed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int expectCursor;
    private volatile bool timedOut;
    private volatile bool ended;

    internal SformProcess(
        Func<Action<SformStream, string>, ChildProcess> start,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        string home,
        TimeSpan timeout,
        Func<string?, string> redact,
        Func<SformResult, bool, Task> completed,
        CancellationToken cancellationToken)
    {
        Arguments = arguments;
        CommandLine = $"sform {CommandLines.Display(arguments)}";
        WorkingDirectory = workingDirectory;
        Home = home;
        this.redact = redact;
        this.completed = completed;
        stopwatch = Stopwatch.StartNew();
        child = start(OnOutput);
        IsTerminal = child.IsTerminal;
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lifetime.CancelAfter(timeout);
        Timeout = timeout;
        lifetime.Token.Register(OnTimeout);
        finished = FinishAsync();
    }

    public IReadOnlyList<string> Arguments { get; }

    public string CommandLine { get; }

    public string WorkingDirectory { get; }

    public string Home { get; }

    public bool IsTerminal { get; }

    public TimeSpan Timeout { get; }

    public int ProcessId => child.Id;

    public bool HasExited => ended;

    // Completes when sform ended (also killed), with what it wrote; never throws, unlike WaitForExitAsync.
    public Task<SformResult> Completion => finished;

    // What arrived so far, as written.
    public string StdOut
    {
        get
        {
            lock (gate)
            {
                return stdOut.ToString();
            }
        }
    }

    public string StdErr
    {
        get
        {
            lock (gate)
            {
                return stdErr.ToString();
            }
        }
    }

    // The plain text of the screen so far: the terminal's, or both streams in the order they arrived.
    public string Screen
    {
        get
        {
            lock (gate)
            {
                return PlainScreen();
            }
        }
    }

    // The complete plain lines of a stream so far (Terminal: of both, or of the screen).
    public IReadOnlyList<string> LinesOf(SformStream stream)
    {
        lock (gate)
        {
            return timeline.Where(line => stream == SformStream.Terminal || line.Stream == stream || IsTerminal).Select(line => line.Text).ToArray();
        }
    }

    // The first complete line of the stream (any line so far, not only new ones) that matches; throws a TimeoutException
    // when none came within the timeout, or an SformAssertionException when sform ended without one.
    public async Task<string> WaitForLineAsync(SformStream stream, Func<string, bool> predicate, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        string? found = null;
        await WaitAsync(
            () =>
            {
                found = timeline.FirstOrDefault(line => (stream == SformStream.Terminal || IsTerminal || line.Stream == stream) && predicate(line.Text))?.Text;
                return found is not null;
            },
            $"a line of {stream} that matches",
            timeout);
        return found!;
    }

    // As WaitForLineAsync, but null when sform ends without such a line (e.g. "You are already logged in" instead of a
    // device code).
    public async Task<string?> WaitForLineOrExitAsync(SformStream stream, Func<string, bool> predicate, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        string? found = null;
        var exited = false;
        await WaitAsync(
            () =>
            {
                found = timeline.FirstOrDefault(line => (stream == SformStream.Terminal || IsTerminal || line.Stream == stream) && predicate(line.Text))?.Text;
                exited = ended;
                return found is not null || ended;
            },
            $"a line of {stream} that matches",
            timeout);
        return exited && found is null ? null : found;
    }

    // The text on the screen after the previous expectation (a run in a terminal; for a redirected run, both streams),
    // with any run of white space in text matching any white space or none (a terminal's renderer may move the cursor
    // instead of writing spaces). Returns the match; the next expectation looks after it.
    public Task<Match> ExpectAsync(string text, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var parts = Regex.Split(text.Trim(), @"\s+").Select(Regex.Escape);
        return ExpectAsync(new Regex(string.Join(@"\s*", parts), RegexOptions.CultureInvariant), timeout, $"'{text}'");
    }

    public Task<Match> ExpectAsync(Regex pattern, TimeSpan? timeout = null)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        return ExpectAsync(pattern, timeout, $"/{pattern}/");
    }

    // Types the text: the keyboard of a terminal, the standard input otherwise.
    public Task SendAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return child.WriteAsync(text, lifetime.Token);
    }

    // The text and Enter (CR in a terminal, LF on a redirected input).
    public Task SendLineAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return SendAsync(text + (IsTerminal ? "\r" : "\n"));
    }

    public async Task SendKeyAsync(TerminalKey key, int times = 1)
    {
        var sequence = key == TerminalKey.Enter && !IsTerminal ? "\n" : TerminalKeys.Sequence(key);
        for (var index = 0; index < times; index++)
        {
            await SendAsync(sequence);

            // A prompt reads one key event at a time; a burst of escape sequences could arrive as one.
            if (IsTerminal && times > 1)
            {
                await Task.Delay(50);
            }
        }
    }

    // Writes to the standard input (the keyboard of a terminal).
    public Task WriteAsync(string text)
    {
        return SendAsync(text);
    }

    // Ends the standard input (no effect in a terminal, whose keyboard stays until the end).
    public void CloseInput()
    {
        child.CloseInput();
    }

    // The result once sform ended; throws an SformTimeoutException when the run's timeout (or this one) passed first, after
    // killing the process tree.
    public async Task<SformResult> WaitForExitAsync(TimeSpan? timeout = null)
    {
        if (timeout is { } limit)
        {
            var winner = await Task.WhenAny(finished, Task.Delay(limit));
            if (winner != finished)
            {
                timedOut = true;
                child.KillTree();
            }
        }

        var result = await finished;
        return timedOut ? throw new SformTimeoutException($"{redact(CommandLine)} did not end within {Seconds(timeout ?? Timeout)}; its process tree was killed.\n{result.Describe()}") : result;
    }

    // Kills the process tree and returns what it wrote.
    public async Task<SformResult> KillAsync()
    {
        child.KillTree();
        return await finished;
    }

    public async ValueTask DisposeAsync()
    {
        if (!ended)
        {
            child.KillTree();
        }

        try
        {
            await finished;
        }
        finally
        {
            child.Dispose();
            lifetime.Dispose();
        }
    }

    private static string Seconds(TimeSpan time)
    {
        return $"{time.TotalSeconds:0.#} s";
    }

    private async Task<Match> ExpectAsync(Regex pattern, TimeSpan? timeout, string description)
    {
        Match? found = null;
        await WaitAsync(
            () =>
            {
                var plain = PlainScreen();
                var match = pattern.Match(plain, Math.Min(expectCursor, plain.Length));
                if (!match.Success)
                {
                    return false;
                }

                found = match;
                expectCursor = match.Index + match.Length;
                return true;
            },
            description,
            timeout);
        return found!;
    }

    // Polls the condition (under the lock) on every change of the output, until it holds, sform ends or the timeout passes.
    private async Task WaitAsync(Func<bool> condition, string what, TimeSpan? timeout)
    {
        var deadline = stopwatch.Elapsed + (timeout ?? DefaultWaitTimeout);
        while (true)
        {
            Task signal;
            lock (gate)
            {
                if (condition())
                {
                    return;
                }

                if (ended)
                {
                    break;
                }

                signal = changed.Task;
            }

            var remaining = deadline - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                throw new TimeoutException($"{redact(CommandLine)} wrote no {redact(what)} within {Seconds(timeout ?? DefaultWaitTimeout)}.\n{redact(Describe())}");
            }

            await Task.WhenAny(signal, Task.Delay(remaining));
        }

        var result = await finished;
        throw result.Fail(timedOut ? $"was killed after its timeout of {Seconds(Timeout)} before it wrote {what}" : $"ended before it wrote {what}");
    }

    private string Describe()
    {
        lock (gate)
        {
            return IsTerminal
                ? $"--- terminal so far ---\n{PlainScreen()}"
                : $"--- stdout so far ---\n{AnsiText.Plain(stdOut.ToString())}\n--- stderr so far ---\n{AnsiText.Plain(stdErr.ToString())}";
        }
    }

    // Under the lock.
    private string PlainScreen()
    {
        var raw = screen.ToString();
        return AnsiText.Plain(raw[..AnsiText.CompletePrefixLength(raw)]);
    }

    private void OnOutput(SformStream stream, string text)
    {
        TaskCompletionSource signal;
        lock (gate)
        {
            var isError = stream == SformStream.StdErr;
            (isError ? stdErr : stdOut).Append(text);
            screen.Append(text);
            AddLines(isError ? pendingErr : pendingOut, isError ? SformStream.StdErr : stream == SformStream.Terminal ? SformStream.Terminal : SformStream.StdOut, text, flush: false);
            signal = changed;
            changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        signal.TrySetResult();
    }

    // Under the lock: the complete lines of the text, after what was pending of the stream.
    private void AddLines(StringBuilder pending, SformStream stream, string text, bool flush)
    {
        pending.Append(text);
        var content = pending.ToString();
        var end = flush ? content.Length : content.LastIndexOf('\n') + 1;
        if (end <= 0)
        {
            return;
        }

        var complete = content[..end];
        pending.Clear().Append(content[end..]);
        foreach (var line in AnsiText.Lines(complete))
        {
            timeline.Add(new SformLine(stream, line.TrimEnd(), stopwatch.Elapsed));
        }
    }

    private void OnTimeout()
    {
        if (!ended && lifetime.IsCancellationRequested)
        {
            timedOut = true;
            child.KillTree();
        }
    }

    private async Task<SformResult> FinishAsync()
    {
        int exitCode;
        try
        {
            exitCode = await child.Exited;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            exitCode = int.MinValue;
            OnOutput(SformStream.StdErr, $"\n[the tests could not read the end of the process: {exception.Message}]\n");
        }

        stopwatch.Stop();
        SformResult result;
        TaskCompletionSource signal;
        lock (gate)
        {
            AddLines(pendingOut, IsTerminal ? SformStream.Terminal : SformStream.StdOut, string.Empty, flush: true);
            AddLines(pendingErr, SformStream.StdErr, string.Empty, flush: true);
            ended = true;
            result = new SformResult(
                Arguments,
                CommandLine,
                exitCode,
                stdOut.ToString(),
                stdErr.ToString(),
                timeline.ToArray(),
                stopwatch.Elapsed,
                IsTerminal,
                WorkingDirectory,
                Home,
                redact);
            signal = changed;
        }

        signal.TrySetResult();
        await completed(result, timedOut);
        return result;
    }
}
