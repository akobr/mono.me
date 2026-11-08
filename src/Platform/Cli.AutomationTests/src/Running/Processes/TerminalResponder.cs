using System.Globalization;
using System.Text;

namespace _42.Platform.Cli.AutomationTests.Running.Processes;

// What a terminal emulator does for the program in its pty and util-linux script does not: script copies bytes, it
// answers nothing. .NET on Linux asks the terminal where the cursor is (DSR, ESC [ 6 n) before Sharprompt draws a
// prompt, and waits for the report (ESC [ <row> ; <column> R) on its input: unanswered, it takes the keys the test
// types for the report, and the prompt reads noise. .NET also switches the cursor keys to application mode (DECCKM,
// ESC [ ? 1 h), in which an arrow is ESC O A, not ESC [ A. ConPTY on Windows is a terminal and does both itself.
// The responder follows the output: it tracks the cursor (line feeds, carriage returns, cursor movements and positions,
// enough for the prompts of sform), answers each position request, and rewrites the arrows of the keyboard in
// application mode.
internal sealed class TerminalResponder(int rows, int columns)
{
    private readonly object gate = new();
    private readonly StringBuilder pending = new();
    private int row = 1;
    private int column = 1;
    private bool applicationCursorKeys;

    // The answers the terminal owes for this piece of output, in order: cursor position reports.
    public IReadOnlyList<string> Follow(string output)
    {
        ArgumentNullException.ThrowIfNull(output);

        var answers = new List<string>();

        lock (gate)
        {
            pending.Append(output);
            var text = pending.ToString();
            pending.Clear();

            var index = 0;
            while (index < text.Length)
            {
                var character = text[index];

                if (character == '\u001b')
                {
                    var end = SequenceEnd(text, index);
                    if (end < 0)
                    {
                        // The rest of a sequence comes with the next piece.
                        pending.Append(text, index, text.Length - index);
                        break;
                    }

                    Apply(text.Substring(index, end - index + 1), answers);
                    index = end + 1;
                    continue;
                }

                switch (character)
                {
                    case '\n':
                        row = Math.Min(rows, row + 1);
                        break;
                    case '\r':
                        column = 1;
                        break;
                    case '\b':
                        column = Math.Max(1, column - 1);
                        break;
                    default:
                        if (!char.IsControl(character) && !char.IsLowSurrogate(character))
                        {
                            column++;
                            if (column > columns)
                            {
                                column = 1;
                                row = Math.Min(rows, row + 1);
                            }
                        }

                        break;
                }

                index++;
            }
        }

        return answers;
    }

    // The keys as the terminal sends them in the current cursor key mode.
    public string Keys(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        lock (gate)
        {
            if (!applicationCursorKeys)
            {
                return text;
            }
        }

        return text
            .Replace("\u001b[A", "\u001bOA", StringComparison.Ordinal)
            .Replace("\u001b[B", "\u001bOB", StringComparison.Ordinal)
            .Replace("\u001b[C", "\u001bOC", StringComparison.Ordinal)
            .Replace("\u001b[D", "\u001bOD", StringComparison.Ordinal)
            .Replace("\u001b[H", "\u001bOH", StringComparison.Ordinal)
            .Replace("\u001b[F", "\u001bOF", StringComparison.Ordinal);
    }

    // The index of the last character of the escape sequence that starts at start; -1 when it is not complete yet.
    private static int SequenceEnd(string text, int start)
    {
        if (start + 1 >= text.Length)
        {
            return -1;
        }

        var next = text[start + 1];
        if (next == '[')
        {
            for (var index = start + 2; index < text.Length; index++)
            {
                if (text[index] is >= '@' and <= '~')
                {
                    return index;
                }
            }

            return -1;
        }

        if (next == ']')
        {
            // An operating system command ends with BEL or ST (ESC \).
            for (var index = start + 2; index < text.Length; index++)
            {
                if (text[index] == '\u0007')
                {
                    return index;
                }

                if (text[index] == '\u001b' && index + 1 < text.Length && text[index + 1] == '\\')
                {
                    return index + 1;
                }
            }

            return -1;
        }

        // ESC and one character (ESC =, ESC >, ESC 7, …).
        return start + 1;
    }

    private void Apply(string sequence, List<string> answers)
    {
        if (sequence.Length < 3 || sequence[1] != '[')
        {
            return;
        }

        var final = sequence[^1];
        var parameters = sequence[2..^1];

        switch (final)
        {
            case 'n' when parameters == "6":
                answers.Add(string.Create(CultureInfo.InvariantCulture, $"\u001b[{row};{column}R"));
                break;
            case 'h' when parameters == "?1":
                applicationCursorKeys = true;
                break;
            case 'l' when parameters == "?1":
                applicationCursorKeys = false;
                break;
            case 'H' or 'f':
                var parts = parameters.Split(';');
                row = Clamp(Number(parts, 0), rows);
                column = Clamp(Number(parts, 1), columns);
                break;
            case 'A':
                row = Math.Max(1, row - Number([parameters], 0));
                break;
            case 'B':
                row = Math.Min(rows, row + Number([parameters], 0));
                break;
            case 'C':
                column = Math.Min(columns, column + Number([parameters], 0));
                break;
            case 'D':
                column = Math.Max(1, column - Number([parameters], 0));
                break;
            case 'G':
                column = Clamp(Number([parameters], 0), columns);
                break;
            case 'd':
                row = Clamp(Number([parameters], 0), rows);
                break;
        }
    }

    // A parameter of a sequence, 1 when it is missing or 0.
    private static int Number(string[] parts, int index)
    {
        return index < parts.Length && int.TryParse(parts[index], NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : 1;
    }

    private static int Clamp(int value, int maximum)
    {
        return Math.Clamp(value, 1, maximum);
    }
}
