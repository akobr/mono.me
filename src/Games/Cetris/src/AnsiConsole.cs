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
