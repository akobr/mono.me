namespace Tetris.Console
{
    public interface IAnsiConsole
    {
        void ClearEntireScreen();

        void ClearScreenUp();

        void MoveCursorToNextLine();

        void MoveCursorToPreviousLine();
    }
}