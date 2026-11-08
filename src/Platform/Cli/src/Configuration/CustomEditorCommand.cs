namespace _42.Platform.Cli.Configuration;

// The command line of a custom editor, or of $VISUAL / $EDITOR: an executable, or a path in quotes, and its arguments.
public static class CustomEditorCommand
{
    // At the first white space, or after the closing quote of an executable in quotes, e.g.
    // "C:\Program Files\Notepad++\notepad++.exe" -multiInst. The executable has no quotes, the arguments are trimmed.
    public static (string Executable, string Arguments) Split(string customCommand)
    {
        var trimmed = customCommand.Trim();

        if (trimmed.Length > 0 && trimmed[0] is '"' or '\'')
        {
            var closing = trimmed.IndexOf(trimmed[0], 1);

            if (closing > 0)
            {
                return (trimmed[1..closing].Trim(), trimmed[(closing + 1)..].Trim());
            }
        }

        var separator = trimmed.IndexOfAny([' ', '\t']);

        return separator < 0
            ? (trimmed, string.Empty)
            : (trimmed[..separator], trimmed[(separator + 1)..].Trim());
    }

    // A command that names an executable: not blank, and not only quotes such as "".
    public static bool IsValid(string? customCommand)
    {
        return !string.IsNullOrWhiteSpace(customCommand) && Split(customCommand).Executable.Length > 0;
    }
}
