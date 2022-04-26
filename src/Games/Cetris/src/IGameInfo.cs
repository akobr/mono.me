namespace Tetris.Console
{
    public interface IGameInfo
    {
        public int Score { get; }

        public int Lines { get; }

        public int Speed { get; }
    }
}
