using System;
using System.Drawing;
using System.IO;
using System.Text;

using SystemConsole = System.Console;

namespace Tetris.Console
{
    class Program
    {
        static void Main(string[] args)
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
            short iBrick = brickManager.AllBricks[1].States[1];
            SystemConsole.WriteLine("iBrick");
            PrintBrick(iBrick);

            short view = state.GetBrickSizeView(GameConstants.INSIDE_BRICK_ZERO_POSITION);
            SystemConsole.WriteLine("view");
            PrintBrick(view);

            SystemConsole.WriteLine("AND");
            PrintBrick((short)(view & iBrick));
        }

        private static void PrintBrick(short brick)
        {
            StringBuilder stringRepresentation = new StringBuilder();
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
