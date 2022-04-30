using System;
using System.Collections;
using System.Drawing;

namespace _42.Cetris;

public static class ByteArrayExtensions
{
    public static void Fill(this byte[,] @this, short mask, Point position, byte color)
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
                var bit = bitsOfMask[new Point(r, c).ToFlatIndex(4)];

                if (bit)
                {
                    @this[gamePosition.X, gamePosition.Y] = color;
                }
            }
        }
    }
}
