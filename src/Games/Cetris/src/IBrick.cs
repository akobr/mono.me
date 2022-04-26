using System.Collections.Generic;
using System.Drawing;

namespace Tetris.Console
{
    public interface IBrick
    {
        Color Colour { get; }

        IReadOnlyList<short> States { get; }

        BrickType Type { get; }
    }
}