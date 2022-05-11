using System.Drawing;

namespace _42.Cetris
{
    public static class PointExtensions
    {
        public static bool IsValidPosition(this Point position)
        {
            return position.X is > -2 and < 22
                   && position.Y is > -3 and < 11;
        }

        public static bool IsStartPosition(this Point position)
        {
            return position == GameConstants.BRICK_START_POSITION;
        }

        public static int ToFlatIndex(this Point position, int columnsCount = GameConstants.GAME_COLUMNS_COUNT)
        {
            return (position.X * columnsCount) + position.Y;
        }

        public static Point ToRectangularIndex(this int flatIndex, int columnsCount = GameConstants.GAME_COLUMNS_COUNT)
        {
            return new Point(flatIndex / columnsCount, flatIndex % columnsCount);
        }
    }
}
