using System.Drawing;

namespace _42.Cetris;

public static class GameConstants
{
    public const int GAME_ROWS_COUNT = 20;
    public const int GAME_COLUMNS_COUNT = 10;
    public const int BRICK_SIZE = 4;

    public static readonly Point BRICK_START_POSITION = new(0, 5);
    public static readonly Point INSIDE_BRICK_ZERO_POSITION = new(1, 2);
}
