namespace _42.Platform.Cli.AutomationTests.Running.Terminal;

// The input sequences of TerminalKey, as an xterm in normal cursor mode sends them.
public static class TerminalKeys
{
    public static string Sequence(TerminalKey key)
    {
        return key switch
        {
            TerminalKey.Enter => "\r",
            TerminalKey.Tab => "\t",
            TerminalKey.Space => " ",
            TerminalKey.Escape => "\u001b",
            TerminalKey.Backspace => "\u007f",
            TerminalKey.Delete => "\u001b[3~",
            TerminalKey.Up => "\u001b[A",
            TerminalKey.Down => "\u001b[B",
            TerminalKey.Right => "\u001b[C",
            TerminalKey.Left => "\u001b[D",
            TerminalKey.Home => "\u001b[H",
            TerminalKey.End => "\u001b[F",
            TerminalKey.PageUp => "\u001b[5~",
            TerminalKey.PageDown => "\u001b[6~",
            TerminalKey.CtrlC => "\u0003",
            TerminalKey.CtrlD => "\u0004",
            _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Not a key of TerminalKey."),
        };
    }
}
