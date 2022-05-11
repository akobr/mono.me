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

        private readonly int _rowCount = GameConstants.GAME_ROWS_COUNT;
        private readonly int _columnCount = GameConstants.GAME_COLUMNS_COUNT;

        public GameState()
        {
            _bits = new BitArray(_rowCount * _columnCount);
            _colors = new byte[_rowCount, _columnCount];
        }

        public GameState(GameStateModel model)
        {
            _bits = new BitArray(model.Bytes);
            _colors = new byte[_rowCount, _columnCount];

            var index = -1;
            foreach (var color in model.Colors)
            {
                var position = (++index).ToRectangularIndex();
                _colors[position.X, position.Y] = color;
            }
        }

        public BitArray Bits => _bits;

        public byte[,] Colors => _colors;

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

        public int Fill(short mask, Point position, byte color)
        {
            if (!position.IsValidPosition())
            {
                return 0;
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

            var fullRowsCount = 0;

            for (var r = _rowCount - 1; r >= Math.Max(startRowIndex, 0); r--)
            {
                var isFullRow = true;

                for (var c = 0; c < _columnCount; c++)
                {
                    var gamePosition = new Point(r, c);
                    if (!_bits[gamePosition.ToFlatIndex()])
                    {
                        isFullRow = false;
                        break;
                    }
                }

                if (isFullRow)
                {
                    fullRowsCount++;
                    RemoveRow(r);
                    r++;
                }
            }

            return fullRowsCount;
        }

        public byte[,] Clone()
        {
            var copy = new byte[_rowCount, _columnCount];
            Array.Copy(_colors, copy, _colors.Length);
            return copy;
        }

        private void RemoveRow(int row)
        {
            for (var r = row - 1; r >= 0; r--)
            {
                for (var c = 0; c < _columnCount; c++)
                {
                    var gamePosition = new Point(r, c);
                    var flatIndex = gamePosition.ToFlatIndex();
                    _bits[flatIndex + _columnCount] = _bits[flatIndex];
                    _colors[r + 1, c] = _colors[r, c];
                }
            }

            for (var c = 0; c < _columnCount; c++)
            {
                _bits[_columnCount] = false;
                _colors[0, c] = 0;
            }
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
