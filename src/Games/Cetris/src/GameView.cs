using System;
using System.Drawing;
using System.Threading;

using SystemConsole = System.Console;

namespace _42.Cetris
{
    public class GameView : IGameView, IDisposable
    {
        private readonly object _locker = new();
        private readonly IAnsiConsole _ansiConsole;
        private readonly Timer _inputTimer;

        public GameView(IAnsiConsole ansiConsole)
        {
            _ansiConsole = ansiConsole ?? throw new ArgumentNullException(nameof(ansiConsole));
            _inputTimer = new Timer(OnInputTimerElapsed, null, 0, 100);
            SystemConsole.CancelKeyPress += OnConsoleCancelKeyPress;
        }

        public event EventHandler<ConsoleKey>? OnKeyPressed;

        public event EventHandler? OnClosing;

        public void RenderGame(byte[,] game, IGameInfo info, TimeSpan gameClock, bool isPaused, bool isGameOver)
        {
            lock (_locker)
            {
                RenderContentRectangle(game);

                var tmpColor = SystemConsole.ForegroundColor;
                SystemConsole.ForegroundColor = ConsoleColor.White;
                PrintText($"Score: {info.Score} ({info.Lines})", new Point(2, 24));
                PrintText($"Speed: {info.Speed}", new Point(3, 24));
                PrintText($"Time:  {info.Time + gameClock:hh\\:mm\\:ss}", new Point(4, 24));

                if (isGameOver)
                {
                    SystemConsole.ForegroundColor = ConsoleColor.Red;
                    PrintText("Game over, press R to restart.", new Point(6, 24));
                }
                else if (isPaused)
                {
                    SystemConsole.ForegroundColor = ConsoleColor.Yellow;
                    PrintText("Game paused, press SPACE to continue.", new Point(6, 24));
                }

                SystemConsole.ForegroundColor = tmpColor;
                _ansiConsole.SetCursor(new Point(21, 24));
            }
        }

        private void PrintText(string text, Point position)
        {
            _ansiConsole.SetCursor(position);
            SystemConsole.Write(text);
        }

        private void RenderContentRectangle(byte[,] game)
        {
            var lastRowIndex = game.GetUpperBound(0);
            var lastColumnIndex = game.GetUpperBound(1);
            var contentWidth = (lastColumnIndex + 1) * 2;

            _ansiConsole.MoveCursorToBeginning();

            SystemConsole.Write('┌');
            SystemConsole.Write(new string('─', contentWidth));
            SystemConsole.WriteLine('┐');

            for (var r = 0; r <= lastRowIndex; r++)
            {
                SystemConsole.Write('│');

                for (var c = 0; c <= lastColumnIndex; c++)
                {
                    var cell = game[r, c];

                    if (cell == 0)
                    {
                        SystemConsole.Write("  ");
                    }
                    else
                    {
                        var tmpColor = SystemConsole.ForegroundColor;
                        SystemConsole.ForegroundColor = (ConsoleColor)cell; // TODO
                        SystemConsole.Write("██");
                        SystemConsole.ForegroundColor = tmpColor;
                    }
                }

                SystemConsole.WriteLine('│');
            }

            SystemConsole.Write('└');
            SystemConsole.Write(new string('─', contentWidth));
            SystemConsole.WriteLine('┘');
        }

        private void OnInputTimerElapsed(object? state)
        {
            while (SystemConsole.KeyAvailable)
            {
                var key = SystemConsole.ReadKey(true);
                OnKeyPressed?.Invoke(this, key.Key);
            }
        }

        private void OnConsoleCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            OnClosing?.Invoke(this, EventArgs.Empty);
            e.Cancel = true;
        }

        public void Dispose()
        {
            _inputTimer.Dispose();
            SystemConsole.CancelKeyPress -= OnConsoleCancelKeyPress;
        }
    }
}
