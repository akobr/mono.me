namespace _42.Platform.Cli.AutomationTests.Running.Processes;

// A started sform, whatever runs it: redirected standard streams (RedirectedChild), a Windows pseudo console
// (ConPtyChild) or util-linux script (ScriptChild). Output arrives as decoded text through the sink given at the start,
// from the reader threads; Exited completes when the process ended and every byte of its output was delivered.
internal abstract class ChildProcess : IDisposable
{
    public abstract int Id { get; }

    public abstract bool IsTerminal { get; }

    // The exit code once the process ended and its output was read to the end.
    public abstract Task<int> Exited { get; }

    // Keyboard input in a terminal, the standard input otherwise.
    public abstract Task WriteAsync(string text, CancellationToken cancellationToken);

    // Closes the standard input (a pseudo terminal keeps its keyboard until the end).
    public abstract void CloseInput();

    // Kills the process and everything it started; never throws.
    public abstract void KillTree();

    public abstract void Dispose();
}
