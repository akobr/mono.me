using System;
using System.Collections;
using System.Drawing;

namespace Tetris.Console
{
    public class GameState
    {
        // 20 * 10 = 200 / 8 = 25 bytes for entire game state (without colours)
        private BitArray bits;
        private Color[,] colours;

        private int rowCount;
        private int columnCount;

        public GameState(int rowCount = GameConstants.GAME_ROWS_COUNT, int columnCount = GameConstants.GAME_COLUMNS_COUNT)
        {
            this.rowCount = rowCount > 0 ? rowCount : throw new ArgumentOutOfRangeException(nameof(rowCount));
            this.columnCount = columnCount > 0 ? columnCount : throw new ArgumentOutOfRangeException(nameof(columnCount));
            bits = new BitArray(rowCount * columnCount);
            colours = new Color[rowCount, columnCount];
        }

        public Color GetColour(int rowIndex, int columnIndex)
        {
            CheckBounds(rowIndex, columnIndex);
            return colours[rowIndex, columnIndex];
        }

        public bool Get(int rowIndex, int columnIndex)
        {
            CheckBounds(rowIndex, columnIndex);
            return bits[new Point(rowIndex, columnIndex).ToFlatIndex()];
        }

        public bool Set(int rowIndex, int columnIndex, bool value)
        {
            CheckBounds(rowIndex, columnIndex);
            return bits[new Point(rowIndex, columnIndex).ToFlatIndex()] = value;
        }

        public bool this[int rowIndex, int columnIndex]
        {
            get { return Get(rowIndex, columnIndex); }
            set { Set(rowIndex, columnIndex, value); }
        }

        public short GetBrickSizeView(Point position)
        {
            if (!position.IsValidPosition())
            {
                return 0;
            }

            int startRowIndex = position.X - GameConstants.INSIDE_BRICK_ZERO_POSITION.X;
            int startColumnIndex = position.Y - GameConstants.INSIDE_BRICK_ZERO_POSITION.Y;

            short view = 0;
            int count = 16;

            for (int r = startRowIndex; r < startColumnIndex + GameConstants.BRICK_SIZE; r++)
            {
                for (int c = startColumnIndex; c < startColumnIndex + GameConstants.BRICK_SIZE; c++)
                {
                    --count;

                    if (bits[new Point(r, c).ToFlatIndex()])
                    {
                        view |= (short)(1 << count);
                    }
                }
            }

            return view;
        }

        public void Fill(short mask, Point position, Color colour)
        {
            if (!position.IsValidPosition())
            {
                return;
            }

            int startRowIndex = position.X - GameConstants.INSIDE_BRICK_ZERO_POSITION.X;
            int startColumnIndex = position.Y - GameConstants.INSIDE_BRICK_ZERO_POSITION.Y;

            BitArray bitsOfMask = new BitArray(BitConverter.GetBytes(mask));

            for (int r = 0; r < GameConstants.BRICK_SIZE; r++)
            {
                for (int c = 0; c < GameConstants.BRICK_SIZE; c++)
                {
                    Point gamePosition = new Point(startRowIndex + r, startColumnIndex + c);
                    bits[gamePosition.ToFlatIndex()] = bitsOfMask[new Point(r, c).ToFlatIndex()];
                    colours[gamePosition.X, gamePosition.Y] = colour;
                }
            }
        }

        private void CheckBounds(int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || rowIndex >= rowCount)
            {
                throw new IndexOutOfRangeException(nameof(rowIndex));
            }

            if (columnIndex < 0 || columnIndex >= columnCount)
            {
                throw new IndexOutOfRangeException(nameof(columnIndex));
            }
        }
    }
}
