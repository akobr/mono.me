using System.Drawing;

using SystemConsole = System.Console;

namespace _42.Cetris;

public class AnsiConsole : IAnsiConsole
{
    private const string EscapeCharacter = "\u001b";

    public void ClearEntireScreen()
    {
        SystemConsole.Write($"{EscapeCharacter}[2J");
    }

    public void ClearScreenUp()
    {
        SystemConsole.Write($"{EscapeCharacter}[1J");
    }

    public void ShowCursor()
    {
        SystemConsole.Write($"{EscapeCharacter}[25h");
    }

    public void HideCursor()
    {
        SystemConsole.Write($"{EscapeCharacter}[25l");
    }

    public void SetCursor(Point position)
    {
        SystemConsole.Write($"{EscapeCharacter}[{position.X};{position.Y}H");
    }

    public void MoveCursorToBeginning()
    {
        SystemConsole.Write($"{EscapeCharacter}[0;0H");
    }

    public void MoveCursorToPreviousLine()
    {
        SystemConsole.Write($"{EscapeCharacter}[1F");
    }

    public void MoveCursorToNextLine()
    {
        SystemConsole.Write($"{EscapeCharacter}[1E");
    }
}
