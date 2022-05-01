using System;
using System.Collections;
using System.Drawing;

namespace _42.Cetris
{
    public class GameState
    {
        // 20 * 10 = 200 / 8 = 25 bytes for entire game state (without colors)
        private readonly BitArray _bits;
        private readonly byte[,] _colors;

        private readonly int _rowCount;
        private readonly int _columnCount;

        public GameState(int rowCount = GameConstants.GAME_ROWS_COUNT, int columnCount = GameConstants.GAME_COLUMNS_COUNT)
        {
            _rowCount = rowCount > 0 ? rowCount : throw new ArgumentOutOfRangeException(nameof(rowCount));
            _columnCount = columnCount > 0 ? columnCount : throw new ArgumentOutOfRangeException(nameof(columnCount));
            _bits = new BitArray(rowCount * columnCount);
            _colors = new byte[rowCount, columnCount];
        }

        public bool this[int rowIndex, int columnIndex]
        {
            get => Get(rowIndex, columnIndex);
            set => Set(rowIndex, columnIndex, value);
        }

        public byte GetColor(int rowIndex, int columnIndex)
        {
            CheckBounds(rowIndex, columnIndex);
            return _colors[rowIndex, columnIndex];
        }

        public bool Get(int rowIndex, int columnIndex)
        {
            CheckBounds(rowIndex, columnIndex);
            return _bits[new Point(rowIndex, columnIndex).ToFlatIndex()];
        }

        public bool Set(int rowIndex, int columnIndex, bool value)
        {
            CheckBounds(rowIndex, columnIndex);
            return _bits[new Point(rowIndex, columnIndex).ToFlatIndex()] = value;
        }

        public short GetBrickSizeView(Point position)
        {
            if (!position.IsValidPosition())
            {
                return 0;
            }

            var startRowIndex = position.X - GameConstants.INSIDE_BRICK_ZERO_POSITION.X;
            var startColumnIndex = position.Y - GameConstants.INSIDE_BRICK_ZERO_POSITION.Y;

            short view = 0;
            var count = 16;

            for (var r = startRowIndex; r < startRowIndex + GameConstants.BRICK_SIZE; r++)
            {
                for (var c = startColumnIndex; c < startColumnIndex + GameConstants.BRICK_SIZE; c++)
                {
                    --count;

                    if (r is < 0 or > 19
                        || c is < 0 or > 9
                        || _bits[new Point(r, c).ToFlatIndex()])
                    {
                        view |= (short)(1 << count);
                    }
                }
            }

            return view;
        }

        public void Fill(short mask, Point position, byte color)
        {
            if (!position.IsValidPosition())
            {
                return;
            }

            var startRowIndex = position.X - GameConstants.INSIDE_BRICK_ZERO_POSITION.X;
            var startColumnIndex = position.Y - GameConstants.INSIDE_BRICK_ZERO_POSITION.Y;

            var bitsOfMask = new BitArray(BitConverter.GetBytes(mask));
            for (var r = 0; r < GameConstants.BRICK_SIZE; r++)
            {
                for (var c = 0; c < GameConstants.BRICK_SIZE; c++)
                {
                    var gamePosition = new Point(startRowIndex + r, startColumnIndex + c);
                    var isGamePositionInPlayground = gamePosition.X is >= 0 and < 20 && gamePosition.Y is >= 0 and < 10;
                    var gamePositionFlat = gamePosition.ToFlatIndex();
                    var gameBit = !isGamePositionInPlayground || _bits[gamePositionFlat];
                    var maskBit = bitsOfMask[15 - new Point(r, c).ToFlatIndex(GameConstants.BRICK_SIZE)];

                    if (!isGamePositionInPlayground)
                    {
                        continue;
                    }

                    if (!gameBit && maskBit)
                    {
                        _colors[gamePosition.X, gamePosition.Y] = color;
                    }

                    _bits[gamePositionFlat] = maskBit || gameBit;
                }
            }
        }

        public byte[,] Clone()
        {
            var copy = new byte[_rowCount, _columnCount];
            Array.Copy(_colors, copy, _colors.Length);
            return copy;
        }

        private void CheckBounds(int rowIndex, int columnIndex)
        {
            if (rowIndex < 0 || rowIndex >= _rowCount)
            {
                throw new IndexOutOfRangeException(nameof(rowIndex));
            }

            if (columnIndex < 0 || columnIndex >= _columnCount)
            {
                throw new IndexOutOfRangeException(nameof(columnIndex));
            }
        }
    }
}
