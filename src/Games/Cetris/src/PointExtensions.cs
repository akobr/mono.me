using System.Drawing;

namespace Tetris.Console
{
    public static class PointExtensions
    {
        public static bool IsValidPosition(this Point position)
        {
            return 0 < position.X && position.X < 18
                && 1 < position.Y && position.Y < 9;
        }

        public static bool IsStartPosition(this Point position)
        {
            return GameConstants.BRICK_START_POSITION == position;
        }

        public static int ToFlatIndex(this Point position, int columnsCount = GameConstants.GAME_COLUMNS_COUNT)
        {
            return position.X * columnsCount + position.Y;
        }
    }
}
