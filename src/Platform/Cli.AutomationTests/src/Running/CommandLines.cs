using System.Text;

namespace _42.Platform.Cli.AutomationTests.Running;

// Command lines from argument lists. A redirected run passes the arguments as a list (ProcessStartInfo.ArgumentList, no
// shell); these are for the places that need one string: a message (Display), CreateProcess of a pseudo console
// (Windows) and the shell command of util-linux script (PosixShell).
public static class CommandLines
{
    // For a reader: an argument with a space, a quote or a shell character in single quotes.
    public static string Display(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return string.Join(' ', arguments.Select(argument => NeedsQuotes(argument) ? PosixQuote(argument) : argument));
    }

    // Every argument single-quoted for /bin/sh: a ' becomes '\''. Nothing inside single quotes is special to the shell.
    public static string PosixShell(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        return string.Join(' ', arguments.Select(PosixQuote));
    }

    public static string PosixQuote(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        return $"'{argument.Replace("'", "'\\''", StringComparison.Ordinal)}'";
    }

    // The command line CreateProcess hands to the C runtime's argv parser (and CommandLineToArgvW): the rules of
    // .NET's own PasteArguments. Backslashes are literal unless they precede a quote; then they double, and the quote is
    // escaped.
    public static string Windows(IEnumerable<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var builder = new StringBuilder();
        foreach (var argument in arguments)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            AppendWindowsArgument(builder, argument);
        }

        return builder.ToString();
    }

    private static void AppendWindowsArgument(StringBuilder builder, string argument)
    {
        if (argument.Length > 0 && !argument.Any(character => char.IsWhiteSpace(character) || character == '"'))
        {
            builder.Append(argument);
            return;
        }

        builder.Append('"');
        var index = 0;
        while (index < argument.Length)
        {
            var character = argument[index++];
            if (character == '\\')
            {
                var backslashes = 1;
                while (index < argument.Length && argument[index] == '\\')
                {
                    index++;
                    backslashes++;
                }

                if (index == argument.Length)
                {
                    // Before the closing quote: doubled.
                    builder.Append('\\', backslashes * 2);
                }
                else if (argument[index] == '"')
                {
                    builder.Append('\\', (backslashes * 2) + 1).Append('"');
                    index++;
                }
                else
                {
                    builder.Append('\\', backslashes);
                }

                continue;
            }

            if (character == '"')
            {
                builder.Append('\\').Append('"');
                continue;
            }

            builder.Append(character);
        }

        builder.Append('"');
    }

    private static bool NeedsQuotes(string argument)
    {
        return argument.Length == 0 || argument.Any(character => char.IsWhiteSpace(character) || "'\"\\$`!*?&|;<>(){}[]#~".Contains(character, StringComparison.Ordinal));
    }
}
