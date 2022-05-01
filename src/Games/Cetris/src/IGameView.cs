using System;
using System.Drawing;

namespace _42.Cetris
{
    public interface IGameView
    {
        event EventHandler? OnClosing;

        event EventHandler<ConsoleKey>? OnKeyPressed;

        void RenderGame(byte[,] game, IGameInfo info);

        void PrintBrick(short brick, Point position);

        void PrintText(string text, Point position);
    }
}
