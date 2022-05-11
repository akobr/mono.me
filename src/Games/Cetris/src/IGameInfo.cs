using System;

namespace _42.Cetris
{
    public interface IGameInfo
    {
        int Score { get; }

        int Lines { get; }

        int Speed { get; }

        TimeSpan Time { get; }
    }
}
