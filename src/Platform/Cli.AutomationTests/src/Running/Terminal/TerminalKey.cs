namespace _42.Platform.Cli.AutomationTests.Running.Terminal;

// A key a test presses in a pseudo terminal (SformProcess.SendKeyAsync): the input sequence a VT terminal sends for it,
// which the pseudo console turns into the key event Sharprompt's prompts read (a selection moves with Up and Down, Enter
// picks, Space toggles a multi-selection, Escape or Ctrl+C cancels).
public enum TerminalKey
{
    Enter,
    Tab,
    Space,
    Escape,
    Backspace,
    Delete,
    Up,
    Down,
    Left,
    Right,
    Home,
    End,
    PageUp,
    PageDown,
    CtrlC,
    CtrlD,
}
