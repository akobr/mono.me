using System;
using System.Drawing;
using System.Threading.Tasks;

using SystemConsole = System.Console;

namespace _42.Cetris
{
    internal static class Program
    {
        public static async Task Main(string[] args)
        {
            var console = new AnsiConsole();
            using var view = new GameView(console);
            using var game = new GameLogic(view);

            console.ClearEntireScreen();
            console.HideCursor();
            SystemConsole.CursorVisible = false;

            await game.PlayAsync();

            SystemConsole.CursorVisible = true;
            console.ShowCursor();

            console.SetCursor(new Point(23, 0));
            SystemConsole.WriteLine();
            SystemConsole.WriteLine("The game saved.");
            SystemConsole.Write("To continue, simply run ");
            var tmp = SystemConsole.ForegroundColor;
            SystemConsole.ForegroundColor = ConsoleColor.Magenta;
            SystemConsole.Write("cetris");
            SystemConsole.ForegroundColor = tmp;
            SystemConsole.WriteLine(" again.");
        }
    }
}
