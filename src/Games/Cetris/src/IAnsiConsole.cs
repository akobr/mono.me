namespace _42.Cetris
{
    public interface IAnsiConsole
    {
        void ClearEntireScreen();

        void ClearScreenUp();

        void MoveCursorToBeginning();

        void MoveCursorToNextLine();

        void MoveCursorToPreviousLine();
    }
}
