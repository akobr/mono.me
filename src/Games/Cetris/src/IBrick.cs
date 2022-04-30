using System.Collections.Generic;

namespace _42.Cetris
{
    public interface IBrick
    {
        byte Color { get; }

        IReadOnlyList<short> States { get; }

        BrickType Type { get; }
    }
}
