using System.IO.Abstractions;
using System.Runtime.Versioning;

namespace _42.Platform.Cli.AutomationTests.Running.Processes;

// sform in a pseudo terminal on Linux without a package: util-linux script runs it on a new pty
// (script -q -e -f -c '<command>' /dev/null): what sform draws arrives on script's standard output, and what the test
// writes to script's standard input is typed on the pty. -e returns sform's exit code, -f flushes every write, -q drops
// script's own lines, /dev/null keeps no typescript. The command runs under /bin/sh with every argument single-quoted
// (CommandLines.PosixShell) after stty sets the pty's size (script's standard input is no terminal to copy it from).
// macOS ships BSD script, whose options differ; it is not supported (TerminalSupport says why). script answers nothing
// the program asks its terminal, so a TerminalResponder answers the cursor position requests of .NET and sends the arrows
// in the cursor key mode the program set.
[SupportedOSPlatform("linux")]
internal sealed class ScriptChild : ChildProcess
{
    public const string ScriptPath = "/usr/bin/script";

    private readonly SemaphoreSlim writing = new(1, 1);
    private readonly TerminalResponder responder;
    private RedirectedChild inner = null!;

    private ScriptChild(TerminalResponder responder)
    {
        this.responder = responder;
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

        var child = new ScriptChild(new TerminalResponder(request.Rows, request.Columns));
        child.inner = RedirectedChild.Start(
            request with { FileName = script, Arguments = ["-q", "-e", "-f", "-c", command, "/dev/null"], Environment = environment },
            (stream, text) =>
            {
                sink(SformStream.Terminal, text);
                foreach (var answer in child.responder.Follow(text))
                {
                    _ = child.AnswerAsync(answer);
                }
            },
            fileSystem);
        return child;
    }

    public override async Task WriteAsync(string text, CancellationToken cancellationToken)
    {
        await writing.WaitAsync(cancellationToken);
        try
        {
            await inner.WriteAsync(responder.Keys(text), cancellationToken);
        }
        finally
        {
            writing.Release();
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
        writing.Dispose();
    }

    // A report the program asked for; lost when the process has ended meanwhile.
    private async Task AnswerAsync(string answer)
    {
        try
        {
            await writing.WaitAsync();
            try
            {
                await inner.WriteAsync(answer, CancellationToken.None);
            }
            finally
            {
                writing.Release();
            }
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // the program is gone
        }
    }
}
