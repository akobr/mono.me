using System.IO.Abstractions;
using System.Runtime.Versioning;
using _42.Platform.Cli.AutomationTests.Running.Terminal;

namespace _42.Platform.Cli.AutomationTests.Running.Processes;

// sform in a pseudo terminal on Linux without a package: util-linux script runs it on a new pty
// (script -q -e -f -c '<command>' /dev/null): what sform draws arrives on script's standard output, and what the test
// writes to script's standard input is typed on the pty. -e returns sform's exit code, -f flushes every write, -q drops
// script's own lines, /dev/null keeps no typescript. The command runs under /bin/sh with every argument single-quoted
// (CommandLines.PosixShell) after stty sets the pty's size (script's standard input is no terminal to copy it from).
// script is no terminal emulator, so the answer a terminal gives to the cursor position query (ESC [ 6 n, which .NET's
// Console writes for every Sharprompt prompt) is typed here, with the position TerminalCursor follows on the output;
// unanswered, .NET takes the keys of the test as part of the answer. The answers and the test's input are written one
// at a time, so an answer never lands inside a key's sequence.
// macOS ships BSD script, whose options differ; it is not supported (TerminalSupport says why).
[SupportedOSPlatform("linux")]
internal sealed class ScriptChild : ChildProcess
{
    public const string ScriptPath = "/usr/bin/script";

    private readonly TerminalCursor cursor;
    private readonly SemaphoreSlim typing = new(1, 1);
    private readonly TaskCompletionSource<RedirectedChild> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private RedirectedChild inner = null!;

    private ScriptChild(TerminalCursor cursor)
    {
        this.cursor = cursor;
    }

    public override int Id => inner.Id;

    public override bool IsTerminal => true;

    public override Task<int> Exited => inner.Exited;

    public static ScriptChild Start(StartRequest request, Action<SformStream, string> sink, IFileSystem fileSystem)
    {
        var script = new[] { ScriptPath, "/bin/script" }.FirstOrDefault(fileSystem.File.Exists)
            ?? throw new PlatformNotSupportedException("A pseudo terminal on Linux needs util-linux script (/usr/bin/script, package util-linux or bsdutils).");

        var command = $"stty cols {request.Columns} rows {request.Rows} 2>/dev/null; exec {CommandLines.PosixShell([request.FileName, .. request.Arguments])}";
        var environment = new Dictionary<string, string>(request.Environment, StringComparer.Ordinal)
        {
            ["SHELL"] = "/bin/sh",
            ["TERM"] = request.Environment.TryGetValue("TERM", out var term) ? term : "xterm-256color",
            ["LINES"] = request.Rows.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        // The output starts before Start returns: an answer waits for the started child.
        var child = new ScriptChild(new TerminalCursor(request.Columns, request.Rows));
        child.inner = RedirectedChild.Start(
            request with { FileName = script, Arguments = ["-q", "-e", "-f", "-c", command, "/dev/null"], Environment = environment },
            (stream, text) =>
            {
                sink(SformStream.Terminal, text);
                if (stream == SformStream.StdOut)
                {
                    child.Answer(text);
                }
            },
            fileSystem);
        child.started.SetResult(child.inner);
        return child;
    }

    public override async Task WriteAsync(string text, CancellationToken cancellationToken)
    {
        await typing.WaitAsync(cancellationToken);
        try
        {
            await inner.WriteAsync(text, cancellationToken);
        }
        finally
        {
            typing.Release();
        }
    }

    // The keyboard stays until the end: script ends the session at the end of its input.
    public override void CloseInput()
    {
    }

    public override void KillTree()
    {
        inner.KillTree();
    }

    public override void Dispose()
    {
        inner.Dispose();
    }

    // On the reader of script's standard output (one at a time, in the order of the output).
    private void Answer(string output)
    {
        var reports = cursor.Feed(output);
        if (reports.Length > 0)
        {
            _ = TypeAnswerAsync(reports);
        }
    }

    private async Task TypeAnswerAsync(string reports)
    {
        try
        {
            var child = await started.Task;
            await typing.WaitAsync();
            try
            {
                await child.WriteAsync(reports, CancellationToken.None);
            }
            finally
            {
                typing.Release();
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or ObjectDisposedException)
        {
            // sform ended, or its input is gone: nobody waits for the answer
        }
    }
}
