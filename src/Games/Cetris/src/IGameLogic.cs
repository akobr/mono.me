namespace Tetris.Console
{
    public interface IGameLogic
    {
        bool IsGameOver { get; }
        bool IsPaused { get; }

        void Start();
        void Restart();
        void Pause();
        void Resume();
    }
}