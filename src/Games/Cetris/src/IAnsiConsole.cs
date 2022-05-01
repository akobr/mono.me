using System.Drawing;

namespace _42.Cetris
{
    public interface IAnsiConsole
    {
        void ClearEntireScreen();

        void ClearScreenUp();

        void ShowCursor();

        void HideCursor();

        void SetCursor(Point position);

        void MoveCursorToBeginning();

        void MoveCursorToNextLine();

        void MoveCursorToPreviousLine();
    }
}
