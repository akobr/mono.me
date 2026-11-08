using System.Globalization;
using System.Text;

namespace _42.Platform.Cli.AutomationTests.Running.Terminal;

// Where the cursor of a pseudo terminal stands, for the cursor position report (DSR 6: ESC [ 6 n, answered with
// ESC [ row ; column R) that a terminal sends back when a program asks. util-linux script only copies the pty's output to
// its standard output, so nothing on Linux answers it: .NET's Console on Unix asks for Console.CursorTop, CursorLeft and
// GetCursorPosition, which Sharprompt reads for every prompt, and while it waits for the answer it takes the keys typed
// meanwhile, so the prompt never gets them (ConPTY on Windows answers by itself). ScriptChild feeds the output here and
// types each answer back.
// It follows what moves an xterm's cursor: text (with the pending wrap after the last column), CR, LF, BS and TAB, the CSI
// moves (A B C D E F G H f d, a e `), save and restore (ESC 7 and 8, CSI s and u), index, next line and reverse index,
// all within the size; at the last row a line feed scrolls, so the cursor stays there. Other sequences (colours, erases,
// modes, OSC titles, charsets) do not move it. Every character counts as one column.
internal sealed class TerminalCursor
{
    private readonly int columns;
    private readonly int rows;
    private readonly StringBuilder parameters = new();
    private State state;
    private bool wrapPending;
    private (int Row, int Column, bool WrapPending) saved = (1, 1, false);

    public TerminalCursor(int columns, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        this.columns = columns;
        this.rows = rows;
    }

    private enum State
    {
        Text,
        Escape,
        Csi,
        String,
        Charset,
    }

    // 1-based, as the report says it.
    public int Row { get; private set; } = 1;

    public int Column { get; private set; } = 1;

    // The report a terminal sends for the position now.
    public string Report => string.Create(CultureInfo.InvariantCulture, $"\u001b[{Row};{Column}R");

    // Follows the output, which may end inside an escape sequence (the rest comes with the next text); returns the
    // reports of the position queries in it, each with the position at its query, in order (empty without a query).
    public string Feed(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        StringBuilder? reports = null;
        foreach (var character in text)
        {
            if (Next(character))
            {
                (reports ??= new StringBuilder()).Append(Report);
            }
        }

        return reports?.ToString() ?? string.Empty;
    }

    // True when the character ended a position query.
    private bool Next(char character)
    {
        switch (state)
        {
            case State.Escape:
                return Escape(character);
            case State.Csi:
                return Csi(character);
            case State.String:
                // An OSC, DCS, SOS, PM or APC string ends with BEL or ST (ESC \, which the escape state ends).
                state = character switch
                {
                    '\u0007' => State.Text,
                    '\u001b' => State.Escape,
                    _ => State.String,
                };
                return false;
            case State.Charset:
                state = State.Text;
                return false;
            default:
                Text(character);
                return false;
        }
    }

    private void Text(char character)
    {
        switch (character)
        {
            case '\u001b':
                state = State.Escape;
                break;
            case '\u009b':
                parameters.Clear();
                state = State.Csi;
                break;
            case '\r':
                MoveTo(Row, 1);
                break;
            case '\n' or '\v' or '\f':
                LineFeed();
                break;
            case '\b':
                MoveTo(Row, Column - 1);
                break;
            case '\t':
                MoveTo(Row, ((((Column - 1) / 8) + 1) * 8) + 1);
                break;
            case < ' ' or '\u007f':
                break;

            // The second half of a surrogate pair: the character was counted with the first half.
            case >= '\udc00' and <= '\udfff':
                break;
            default:
                Print();
                break;
        }
    }

    private bool Escape(char character)
    {
        state = State.Text;
        switch (character)
        {
            case '[':
                parameters.Clear();
                state = State.Csi;
                break;
            case ']' or 'P' or 'X' or '^' or '_':
                state = State.String;
                break;
            case '(' or ')' or '*' or '+':
                state = State.Charset;
                break;

            // Intermediates (ESC # 8, ESC SP F) come before the final character.
            case >= ' ' and <= '/':
                state = State.Escape;
                break;
            case '7':
                saved = (Row, Column, wrapPending);
                break;
            case '8':
                (Row, Column, wrapPending) = saved;
                break;
            case 'D':
                LineFeed();
                break;
            case 'E':
                MoveTo(Row, 1);
                LineFeed();
                break;
            case 'M':
                MoveTo(Row - 1, Column);
                break;
            case 'c':
                MoveTo(1, 1);
                saved = (1, 1, false);
                break;
        }

        return false;
    }

    private bool Csi(char character)
    {
        if (character is >= '0' and <= '?')
        {
            parameters.Append(character);
            return false;
        }

        // Intermediates, and control characters a terminal runs in the middle of a sequence, are left out.
        if (character is < '@' or > '~')
        {
            return false;
        }

        state = State.Text;
        var text = parameters.ToString();

        // A private sequence (CSI ? 25 l, CSI > c, CSI ? 6 n) does not move the cursor, nor is it the query.
        if (text.Length > 0 && text[0] is '<' or '=' or '>' or '?')
        {
            return false;
        }

        var values = text.Split(';');
        int Number(int index, int fallback) =>
            index < values.Length && int.TryParse(values[index], NumberStyles.None, CultureInfo.InvariantCulture, out var value) && value > 0
                ? value
                : fallback;

        switch (character)
        {
            case 'A':
                MoveTo(Row - Number(0, 1), Column);
                break;
            case 'B' or 'e':
                MoveTo(Row + Number(0, 1), Column);
                break;
            case 'C' or 'a':
                MoveTo(Row, Column + Number(0, 1));
                break;
            case 'D':
                MoveTo(Row, Column - Number(0, 1));
                break;
            case 'E':
                MoveTo(Row + Number(0, 1), 1);
                break;
            case 'F':
                MoveTo(Row - Number(0, 1), 1);
                break;
            case 'G' or '`':
                MoveTo(Row, Number(0, 1));
                break;
            case 'd':
                MoveTo(Number(0, 1), Column);
                break;
            case 'H' or 'f':
                MoveTo(Number(0, 1), Number(1, 1));
                break;
            case 's':
                saved = (Row, Column, wrapPending);
                break;
            case 'u':
                (Row, Column, wrapPending) = saved;
                break;
            case 'n':
                return Number(0, 0) == 6;
        }

        return false;
    }

    // An xterm writes the last column and waits there: the next character wraps to the next line first.
    private void Print()
    {
        if (wrapPending)
        {
            MoveTo(Row, 1);
            LineFeed();
        }

        if (Column == columns)
        {
            wrapPending = true;
        }
        else
        {
            Column++;
        }
    }

    // At the last row the screen scrolls and the cursor stays.
    private void LineFeed()
    {
        MoveTo(Row + 1, Column);
    }

    private void MoveTo(int row, int column)
    {
        Row = Math.Clamp(row, 1, rows);
        Column = Math.Clamp(column, 1, columns);
        wrapPending = false;
    }
}
