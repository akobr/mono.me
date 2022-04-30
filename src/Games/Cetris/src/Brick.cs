using System.Collections.Generic;

namespace _42.Cetris;

public class Brick : IBrick
{
    public Brick(BrickType type, byte color, params short[] states)
    {
        Type = type;
        Color = color;
        States = states;
    }

    public BrickType Type { get; }

    public byte Color { get; }

    public IReadOnlyList<short> States { get; }

    public override int GetHashCode() => (int)Type;
}
