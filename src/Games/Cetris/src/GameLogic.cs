using System;
using System.Drawing;
using System.Threading;

namespace Tetris.Console
{
    public class GameLogic : IGameLogic, IDisposable
    {
        private readonly BrickManager brickManager;
        private readonly GameState gameState;
        private readonly Timer gameTimer;
        private readonly IGameView view;

        private IBrick? brick;
        private int brickStateIndex;
        private short brickState;
        private Point brickPosition;
        private IBrick nextBrick;

        public GameLogic(IGameView view)
        {
            brickManager = new BrickManager();
            gameState = new GameState();
            gameTimer = new Timer(OnGameIntervalElapsed);

            this.view = view;
            InitialiseGameView();

            nextBrick = brickManager.GetRandomBrick();
        }

        public bool IsGameOver { get; }

        public bool IsPaused { get; }

        public void Start()
        {
            // TODO
            gameTimer.Change(0, 500);
        }

        public void Restart()
        {
            // TODO
        }

        public void Pause()
        {
            // TODO
        }

        public void Resume()
        {
            // TODO
        }

        public void Dispose()
        {
            gameTimer.Dispose();
            view.OnKeyPressed -= OnGameViewKeyPressed;
        }

        private void OnGameIntervalElapsed(object? state)
        {
            if (IsGameOver)
            {
                gameTimer.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }

            ProcessGame();
            RenderGame();
        }

        private void InitialiseGameView()
        {
            view.OnKeyPressed += OnGameViewKeyPressed;
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
                    // TODO: Speed up
                    return;

                case ConsoleKey.Spacebar:
                case ConsoleKey.NumPad0:
                    // TODO: pause
                    return;
            }
        }

        private void ProcessGame()
        {
            if (brick == null)
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
                gameState.Fill(brickState, brickPosition, brick.Colour);
                brick = null;
            }
        }

        private void RenderGame()
        {
            Color[,] game = new Color[GameConstants.GAME_ROWS_COUNT, GameConstants.GAME_COLUMNS_COUNT];
            IGameInfo info = new GameInfo();

            view.RenderGame(game, info);
        }

        private bool IsBrickSettled()
        {
            short affectedView = gameState.GetBrickSizeView(new Point(brickPosition.X + 1, brickPosition.Y));
            return (brickState & affectedView) != 0;
        }

        private bool TryRotate()
        {
            if (brick == null)
            {
                return false;
            }

            short affectedView = gameState.GetBrickSizeView(brickPosition);
            int newBrickStateIndex = GetNextStateIndex(brick, brickStateIndex);
            short newBrickState = brick.States[newBrickStateIndex];

            if ((newBrickState & affectedView) == 0)
            {
                brickState = newBrickState;
                return true;
            }

            return false;
        }

        private bool TryMoveRight()
        {
            return TryMoveTo(new Point(brickPosition.X, brickPosition.Y + 1));
        }

        private bool TryMoveLeft()
        {
            return TryMoveTo(new Point(brickPosition.X, brickPosition.Y - 1));
        }

        private bool TryDrop()
        {
            return TryMoveTo(new Point(brickPosition.X + 1, brickPosition.Y));
        }

        private bool TryMoveTo(Point newPosition)
        {
            if (!newPosition.IsValidPosition())
            {
                return false;
            }

            short affectedView = gameState.GetBrickSizeView(newPosition);

            if ((brickState & affectedView) == 0)
            {
                brickPosition = newPosition;
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
            brick = nextBrick;
            brickStateIndex = 0;
            brickState = brick.States[brickStateIndex];
            nextBrick = brickManager.GetRandomBrick();
        }

        private static int GetNextStateIndex(IBrick brick, int brickStateIndex)
        {
            int newBrickStateIndex = brickStateIndex++;
            return newBrickStateIndex < brick.States.Count
                ? newBrickStateIndex
                : 0;
        }
    }
}
