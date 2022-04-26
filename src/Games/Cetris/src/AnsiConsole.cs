using SystemConsole = System.Console;

namespace Tetris.Console
{
    public class AnsiConsole : IAnsiConsole
    {
        private const string EscapeCharacter = "\u001b";

        public void ClearEntireScreen()
        {
            SystemConsole.Write($"{EscapeCharacter}[J2");
        }

        public void ClearScreenUp()
        {
            SystemConsole.Write($"{EscapeCharacter}[J1");
        }

        public void MoveCursorToPreviousLine()
        {
            SystemConsole.Write($"{EscapeCharacter}[F1");
        }

        public void MoveCursorToNextLine()
        {
            SystemConsole.Write($"{EscapeCharacter}[E1");
        }
    }
}
