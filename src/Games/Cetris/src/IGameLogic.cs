using System.Threading.Tasks;

namespace _42.Cetris
{
    public interface IGameLogic
    {
        bool IsGameOver { get; }

        bool IsPaused { get; }

        Task PlayAsync();

        void Restart();

        void Pause();

        void Resume();
    }
}
