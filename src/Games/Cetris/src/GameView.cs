using System;
using System.Drawing;
using System.Text;
using System.Threading;

using SystemConsole = System.Console;

namespace _42.Cetris
{
    public class GameView : IGameView, IDisposable
    {
        private readonly object _locker = new();
        private readonly IAnsiConsole _ansiConsole;
        private readonly Timer _inputTimer;

        public event EventHandler<ConsoleKey>? OnKeyPressed;

        public event EventHandler? OnClosing;

        public GameView(IAnsiConsole ansiConsole)
        {
            _ansiConsole = ansiConsole ?? throw new ArgumentNullException(nameof(ansiConsole));
            _inputTimer = new Timer(OnInputTimerElapsed, null, 0, 100);
            SystemConsole.CancelKeyPress += OnConsoleCancelKeyPress;
        }

        public void RenderGame(byte[,] game, IGameInfo info)
        {
            lock (_locker)
            {
                RenderContentRectangle(game);
                // TODO: render rest
            }
        }

        public void PrintText(string text, Point position)
        {
            _ansiConsole.SetCursor(position);
            SystemConsole.Write(text);
        }

        public void PrintBrick(short brick, Point position)
        {
            var asText = new StringBuilder();
            asText.Append(Convert.ToString(brick, 2));

            if (asText.Length < 16)
            {
                asText.Insert(0, new string('0', 16 - asText.Length));
            }

            _ansiConsole.SetCursor(position);
            SystemConsole.Write(asText.ToString(0, 4));
            _ansiConsole.SetCursor(new Point(position.X + 1, position.Y));
            SystemConsole.Write(asText.ToString(4, 4));
            _ansiConsole.SetCursor(new Point(position.X + 2, position.Y));
            SystemConsole.Write(asText.ToString(8, 4));
            _ansiConsole.SetCursor(new Point(position.X + 3, position.Y));
            SystemConsole.Write(asText.ToString(12, 4));
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

        private void OnConsoleCancelKeyPress(object sender, ConsoleCancelEventArgs e)
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
