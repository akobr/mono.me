using System;
using System.Drawing;

namespace Tetris.Console
{
    public interface IGameView
    {
        event EventHandler? OnClosing;
        event EventHandler<ConsoleKey>? OnKeyPressed;

        void RenderGame(Color[,] game, IGameInfo info);
    }
}