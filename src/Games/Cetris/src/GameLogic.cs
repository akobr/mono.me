using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace _42.Cetris
{
    public class GameLogic : IGameLogic, IDisposable
    {
        private const string STATE_FILE_NAME = "game.state.json";

        private readonly BrickManager _brickManager;
        private readonly Timer _timer;
        private readonly IGameView _view;
        private readonly TaskCompletionSource _gameTask;
        private readonly Stopwatch _watch;

        private GameState _state;
        private GameInfo _info;
        private IBrick _brick;
        private int _brickStateIndex;
        private short _brickState;
        private Point _brickPosition;
        private IBrick _nextBrick;
        private int _nextBrickStateIndex;
        private bool _isRunning;

        public GameLogic(IGameView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _gameTask = new TaskCompletionSource();
            _info = new GameInfo();
            _brickManager = new BrickManager();
            _state = new GameState();
            _timer = new Timer(OnGameIntervalElapsed, null, Timeout.Infinite, Timeout.Infinite);
            _brick = _nextBrick = _brickManager.GetRandomBrick();
            _watch = new Stopwatch();
            IsGameOver = true;
            InitialiseGameView();
        }

        public bool IsGameOver { get; private set; }

        public bool IsRunning
        {
            get => _isRunning;

            private set
            {
                _isRunning = value;

                if (_isRunning)
                {
                    _watch.Restart();
                }
                else
                {
                    _info.Time += _watch.Elapsed;
                    _watch.Reset();
                }
            }
        }

        public bool IsPaused => !IsRunning;

        public Task PlayAsync()
        {
            if (Restore())
            {
                return _gameTask.Task;
            }

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
            _state = new GameState();
            _nextBrick = _brickManager.GetRandomBrick();
            SpawnNewBrick();
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
            if (IsPaused
                || _info.Speed >= 10)
            {
                return;
            }

            _info.Speed++;

            if (IsRunning)
            {
                _timer.Change(0, GetGamePeriod());
            }
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
            }
            else
            {
                ProcessGame();
            }

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
            Store();
            _gameTask.SetResult();
        }

        private bool Restore()
        {
            if (!File.Exists(STATE_FILE_NAME))
            {
                return false;
            }

            using var fileStream = File.OpenRead(STATE_FILE_NAME);
            var gameStateModel = JsonSerializer.Deserialize<GameStateModel>(fileStream);

            if (gameStateModel is null)
            {
                return false;
            }

            _state = new GameState(gameStateModel);
            _brick = _brickManager.GetBrick(gameStateModel.CurrentBrick.Type);
            _brickStateIndex = gameStateModel.CurrentBrick.StateIndex;
            _brickState = _brick.States[_brickStateIndex];
            _brickPosition = gameStateModel.CurrentBrick.Position is null
                ? GameConstants.BRICK_START_POSITION
                : new Point(gameStateModel.CurrentBrick.Position.Row, gameStateModel.CurrentBrick.Position.Column);
            _nextBrick = _brickManager.GetBrick(gameStateModel.NextBrick.Type);
            _nextBrickStateIndex = gameStateModel.NextBrick.StateIndex;
            _info = new GameInfo
            {
                Score = gameStateModel.Info.Score,
                Lines = gameStateModel.Info.Lines,
                Speed = gameStateModel.Info.Speed,
                Time = gameStateModel.Info.Time,
            };

            IsRunning = false;
            IsGameOver = false;

            RenderGame();
            return true;
        }

        private void Store()
        {
            if (IsGameOver)
            {
                File.Delete(STATE_FILE_NAME);
                return;
            }

            var bytes = new byte[20 * 10 / 8];
            _state.Bits.CopyTo(bytes, 0);

            var gameStateModel = new GameStateModel
            {
                Info = _info,
                Bytes = bytes,
                Colors = _state.Colors.OfType<byte>().ToArray(),
                CurrentBrick = new GameStateModel.BrickModel
                {
                    Type = _brick.Type,
                    StateIndex = _brickStateIndex,
                    Position = new GameStateModel.PointModel
                    {
                        Row = _brickPosition.X,
                        Column = _brickPosition.Y,
                    },
                },
                NextBrick = new GameStateModel.BrickModel
                {
                    Type = _nextBrick.Type,
                    StateIndex = _nextBrickStateIndex,
                },
            };

            using var fileStream = File.Create(STATE_FILE_NAME);
            JsonSerializer.Serialize(fileStream, gameStateModel);
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

                case ConsoleKey.Escape:
                    OnGameEndRequest(null, EventArgs.Empty);
                    return;
            }
        }

        private void ProcessGame()
        {
            if (IsGameOver || TryDrop())
            {
                return;
            }

            if (IsBrickSettled())
            {
                var fullRowCount = _state.Fill(_brickState, _brickPosition, _brick.Color);
                _info.Score += (fullRowCount * 3) + 1;

                if (fullRowCount > 0)
                {
                    _info.Lines += fullRowCount;
                    _info.Speed = Math.Min(_info.Lines / 2, 10);
                    _timer.Change(0, GetGamePeriod());
                }

                SpawnNewBrick();

                if (IsBrickSettled())
                {
                    IsGameOver = true;
                    IsRunning = false;
                }
            }
        }

        private void RenderGame()
        {
            var game = _state.Clone();
            game.Fill(_brickState, _brickPosition, _brick.Color);
            _view.RenderGame(game, _info, _watch.Elapsed, IsPaused, IsGameOver);
        }

        private bool IsBrickSettled()
        {
            var affectedView = _state.GetBrickSizeView(new Point(_brickPosition.X + 1, _brickPosition.Y));
            return (_brickState & affectedView) != 0;
        }

        private bool TryRotate()
        {
            var affectedView = _state.GetBrickSizeView(_brickPosition);
            var newBrickStateIndex = GetNextStateIndex(_brick, _brickStateIndex);
            var newBrickState = _brick.States[newBrickStateIndex];

            if ((newBrickState & affectedView) == 0)
            {
                _brickState = newBrickState;
                _brickStateIndex = newBrickStateIndex;
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
            if (IsPaused)
            {
                return false;
            }

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
            _nextBrickStateIndex = 0;
        }

        private int GetGamePeriod()
        {
            return 1000 - (_info.Speed * 50);
        }

        private static int GetNextStateIndex(IBrick brick, int brickStateIndex)
        {
            var newBrickStateIndex = ++brickStateIndex;
            return newBrickStateIndex < brick.States.Count
                ? newBrickStateIndex
                : 0;
        }
    }
}
