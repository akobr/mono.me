using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using SystemConsole = System.Console;

namespace _42.Cetris
{
    class Program
    {
        public static async Task Main(string[] args)
        {
            SystemConsole.WriteLine(Path.GetRandomFileName());

            GameState state = new GameState();
            state[0, 0] = true;

            state[1, 0] = false;
            state[1, 1] = true;
            state[1, 2] = true;
            state[1, 3] = false;

            state[2, 0] = false;
            state[2, 1] = true;
            state[2, 2] = true;
            state[2, 3] = false;

            SystemConsole.WriteLine();
            SystemConsole.WriteLine(" █ ");
            SystemConsole.WriteLine(" ███ ");
            SystemConsole.WriteLine("   █ ");
            SystemConsole.WriteLine();
            SystemConsole.WriteLine("  ██");
            SystemConsole.WriteLine("  █ ");
            SystemConsole.WriteLine(" ██ ");
            SystemConsole.WriteLine();

            SystemConsole.WriteLine();
            SystemConsole.WriteLine(" ██ ");
            SystemConsole.WriteLine(" ██████ ");
            SystemConsole.WriteLine("     ██ ");
            SystemConsole.WriteLine();
            SystemConsole.WriteLine("   ████ ");
            SystemConsole.WriteLine("   ██ ");
            SystemConsole.WriteLine(" ████ ");
            SystemConsole.WriteLine();

            BrickManager brickManager = new BrickManager();
            short iBrick = brickManager.AllBricks[(int)BrickType.I].States[1];
            SystemConsole.WriteLine("iBrick");
            PrintBrick(iBrick);

            short testView = state.GetBrickSizeView(GameConstants.INSIDE_BRICK_ZERO_POSITION);
            SystemConsole.WriteLine("view");
            PrintBrick(testView);

            SystemConsole.WriteLine("AND");
            PrintBrick((short)(testView & iBrick));

            var console = new AnsiConsole();
            var view = new GameView(console);
            var game = new GameLogic(view);

            console.ClearEntireScreen();
            await game.PlayAsync();
        }

        private static void PrintBrick(short brick)
        {
            var stringRepresentation = new StringBuilder();
            stringRepresentation.Append(Convert.ToString(brick, 2));

            if (stringRepresentation.Length < 16)
            {
                stringRepresentation.Insert(0, new string('0', 16 - stringRepresentation.Length));
            }

            SystemConsole.WriteLine(stringRepresentation.ToString(0, 4));
            SystemConsole.WriteLine(stringRepresentation.ToString(4, 4));
            SystemConsole.WriteLine(stringRepresentation.ToString(8, 4));
            SystemConsole.WriteLine(stringRepresentation.ToString(12, 4));
            SystemConsole.WriteLine();
        }
    }
}
