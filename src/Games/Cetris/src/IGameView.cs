using System;

namespace _42.Cetris
{
    public interface IGameView
    {
        event EventHandler? OnClosing;

        event EventHandler<ConsoleKey>? OnKeyPressed;

        void RenderGame(byte[,] game, IGameInfo info, TimeSpan gameClock, bool isPaused, bool isGameOver);
    }
}
