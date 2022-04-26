using System;
using System.Drawing;
using System.Threading;

using SystemConsole = System.Console;

namespace Tetris.Console
{
    public class GameView : IGameView, IDisposable
    {
        private readonly IAnsiConsole ansiConsole;
        private readonly Timer inputTimer;

        public event EventHandler<ConsoleKey>? OnKeyPressed;
        public event EventHandler? OnClosing;

        public GameView(IAnsiConsole ansiConsole)
        {
            this.ansiConsole = ansiConsole;
            inputTimer = new Timer(OnInputTimerElapsed, null, 0, 100);
            SystemConsole.CancelKeyPress += OnConsoleCancelKeyPress;
        }

        public void RenderGame(Color[,] game, IGameInfo info)
        {
            ansiConsole.ClearEntireScreen();

            // TODO
            SystemConsole.WriteLine("Render");
        }

        private void OnInputTimerElapsed(object? state)
        {
            while (SystemConsole.KeyAvailable)
            {
                ConsoleKeyInfo key = SystemConsole.ReadKey(true);
                OnKeyPressed?.Invoke(this, key.Key);
            }
        }

        private void OnConsoleCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            OnClosing?.Invoke(this, new EventArgs());
            Dispose();
        }

        public void Dispose()
        {
            inputTimer.Dispose();
            SystemConsole.CancelKeyPress -= OnConsoleCancelKeyPress;
        }
    }
}
