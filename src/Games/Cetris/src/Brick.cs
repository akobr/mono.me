using System.Collections.Generic;
using System.Drawing;

namespace Tetris.Console
{
    public class Brick : IBrick
    {
        public Brick(BrickType type, Color colour, params short[] states)
        {
            Type = type;
            Colour = colour;
            States = states;
        }

        public BrickType Type { get; }

        public Color Colour { get; }

        public IReadOnlyList<short> States { get; }
    }
}
