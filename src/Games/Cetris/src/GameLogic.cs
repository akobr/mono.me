using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace _42.Cetris
{
    public class GameLogic : IGameLogic, IDisposable
    {
        private readonly BrickManager _brickManager;
        private readonly GameState _state;
        private readonly Timer _timer;
        private readonly IGameView _view;
        private readonly TaskCompletionSource _gameTask;

        private GameInfo _info;
        private IBrick? _brick;
        private int _brickStateIndex;
        private short _brickState;
        private Point _brickPosition;
        private IBrick _nextBrick;

        public GameLogic(IGameView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _gameTask = new TaskCompletionSource();
            _info = new GameInfo();
            _brickManager = new BrickManager();
            _state = new GameState();
            _timer = new Timer(OnGameIntervalElapsed, null, Timeout.Infinite, Timeout.Infinite);

            IsGameOver = true;
            InitialiseGameView();
        }

        public bool IsGameOver { get; private set; }

        public bool IsRunning { get; private set; }

        public bool IsPaused => !IsRunning;

        public Task PlayAsync()
        {
            if (IsGameOver)
            {
                Start();
            }
            else
            {
                TogglePause();
            }

            return _gameTask.Task;
        }

        public void Start()
        {
            Restart();
            IsRunning = true;
            _timer.Change(0, GetGamePeriod());
        }

        public void Restart()
        {
            _info = new GameInfo();
            _nextBrick = _brickManager.GetRandomBrick();
            IsGameOver = false;
        }

        public void Pause()
        {
            if (IsPaused)
            {
                return;
            }

            IsRunning = false;
        }

        public void Resume()
        {
            if (!IsPaused)
            {
                return;
            }

            if (IsGameOver)
            {
                Start();
                return;
            }

            IsRunning = true;
            _timer.Change(0, GetGamePeriod());
        }

        public void TogglePause()
        {
            if (IsPaused)
            {
                Resume();
                return;
            }

            Pause();
        }

        public void SpeedUp()
        {
            if (_info.Speed >= 10)
            {
                return;
            }

            _info.Speed++;
        }

        public void Dispose()
        {
            _timer.Dispose();
            _view.OnKeyPressed -= OnGameViewKeyPressed;
            _view.OnClosing -= OnGameEndRequest;
        }

        private void OnGameIntervalElapsed(object? state)
        {
            if (!IsRunning)
            {
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }

            ProcessGame();
            RenderGame();
        }

        private void InitialiseGameView()
        {
            _view.OnKeyPressed += OnGameViewKeyPressed;
            _view.OnClosing += OnGameEndRequest;
        }

        private void OnGameEndRequest(object? sender, EventArgs e)
        {
            if (IsGameOver)
            {
                return;
            }

            Pause();
            // TODO: Save
            _gameTask.SetResult();
        }

        private void OnGameViewKeyPressed(object? sender, ConsoleKey e)
        {
            switch (e)
            {
                case ConsoleKey.UpArrow:
                case ConsoleKey.W:
                case ConsoleKey.NumPad8:
                    TryExecuteGameOperation(TryRotate);
                    return;

                case ConsoleKey.LeftArrow:
                case ConsoleKey.A:
                case ConsoleKey.NumPad4:
                    TryExecuteGameOperation(TryMoveLeft);
                    return;

                case ConsoleKey.RightArrow:
                case ConsoleKey.D:
                case ConsoleKey.NumPad6:
                    TryExecuteGameOperation(TryMoveRight);
                    return;

                case ConsoleKey.DownArrow:
                case ConsoleKey.S:
                case ConsoleKey.NumPad2:
                    TryExecuteGameOperation(TryDrop);
                    return;

                case ConsoleKey.E:
                case ConsoleKey.V:
                case ConsoleKey.NumPad1:
                    SpeedUp();
                    return;

                case ConsoleKey.R:
                case ConsoleKey.B:
                case ConsoleKey.NumPad3:
                    Start();
                    return;

                case ConsoleKey.Spacebar:
                case ConsoleKey.NumPad0:
                    TogglePause();
                    return;
            }
        }

        private void ProcessGame()
        {
            if (_brick is null)
            {
                SpawnNewBrick();
                return;
            }

            if (TryDrop())
            {
                return;
            }

            if (IsBrickSettled())
            {
                _state.Fill(_brickState, _brickPosition, _brick.Color);
                _brick = null;
            }
        }

        private void RenderGame()
        {
            var game = _state.Clone();

            if (_brick is not null)
            {
                game.Fill(_brickState, _brickPosition, _brick.Color);
            }

            _view.RenderGame(game, _info);
        }

        private bool IsBrickSettled()
        {
            var affectedView = _state.GetBrickSizeView(new Point(_brickPosition.X + 1, _brickPosition.Y));
            return (_brickState & affectedView) != 0;
        }

        private bool TryRotate()
        {
            if (_brick == null)
            {
                return false;
            }

            var affectedView = _state.GetBrickSizeView(_brickPosition);
            var newBrickStateIndex = GetNextStateIndex(_brick, _brickStateIndex);
            var newBrickState = _brick.States[newBrickStateIndex];

            if ((newBrickState & affectedView) == 0)
            {
                _brickState = newBrickState;
                return true;
            }

            return false;
        }

        private bool TryMoveRight()
        {
            return TryMoveTo(new Point(_brickPosition.X, _brickPosition.Y + 1));
        }

        private bool TryMoveLeft()
        {
            return TryMoveTo(new Point(_brickPosition.X, _brickPosition.Y - 1));
        }

        private bool TryDrop()
        {
            return TryMoveTo(new Point(_brickPosition.X + 1, _brickPosition.Y));
        }

        private bool TryMoveTo(Point newPosition)
        {
            if (!newPosition.IsValidPosition())
            {
                return false;
            }

            var affectedView = _state.GetBrickSizeView(newPosition);

            if ((_brickState & affectedView) == 0)
            {
                _brickPosition = newPosition;
                return true;
            }

            return false;
        }

        private bool TryExecuteGameOperation(Func<bool> operation)
        {
            if (operation())
            {
                RenderGame();
                return true;
            }

            return false;
        }

        private void SpawnNewBrick()
        {
            _brick = _nextBrick;
            _brickStateIndex = 0;
            _brickState = _brick.States[_brickStateIndex];
            _brickPosition = GameConstants.BRICK_START_POSITION;
            _nextBrick = _brickManager.GetRandomBrick();
        }

        private int GetGamePeriod()
        {
            return 1000 - (_info.Speed * 50);
        }

        private static int GetNextStateIndex(IBrick brick, int brickStateIndex)
        {
            var newBrickStateIndex = brickStateIndex++;
            return newBrickStateIndex < brick.States.Count
                ? newBrickStateIndex
                : 0;
        }
    }
}
