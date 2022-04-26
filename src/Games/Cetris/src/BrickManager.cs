using System;
using System.Collections.Generic;
using System.Drawing;

namespace Tetris.Console
{
    public class BrickManager
    {
        private static readonly IBrick[] bricks =
        {
            new Brick(
                BrickType.O,
                Color.Red,
                0b_0000_0110_0110_0000),
            new Brick(
                BrickType.I,
                Color.Green,
                0b_0000_1111_0000_0000,
                0b_0010_0010_0010_0010),
            new Brick(
                BrickType.S,
                Color.Yellow,
                0b_0000_0011_0110_0000,
                0b_0100_0110_0010_0000),
            new Brick(
                BrickType.Z,
                Color.Orange,
                0b_0000_0110_0011_0000,
                0b_0010_0110_0100_0000),
            new Brick(
                BrickType.T,
                Color.Purple,
                0b_0000_0010_0111_0000,
                0b_0001_0011_0001_0000,
                0b_0111_0010_0000_0000,
                0b_0100_0110_0100_0000),
            new Brick(
                BrickType.L,
                Color.Blue,
                0b_0100_0100_0110_0000,
                0b_0000_0001_0111_0000,
                0b_0110_0010_0010_0000,
                0b_0000_0111_0100_0000),
            new Brick(
                BrickType.J,
                Color.Pink,
                0b_0000_0100_0111_0000,
                0b_0010_0010_0110_0000,
                0b_0000_0111_0001_0000,
                0b_0110_0100_0100_0000)
        };

        private readonly Random random;
        private readonly HashSet<IBrick> bagOfTetriminos;
        private readonly Queue<IBrick> lastDraws = new Queue<IBrick>();

        public BrickManager()
        {
            random = new Random();
            bagOfTetriminos = new HashSet<IBrick>(bricks.Length);
            lastDraws = new Queue<IBrick>(bricks.Length);
            InitialiseBag();
        }

        public IBrick GetRandomBrick()
        {
            // TODO
            return bricks[random.Next(0, bricks.Length)];
        }

        public IReadOnlyList<IBrick> AllBricks => bricks;

        private void InitialiseBag()
        {
            foreach (IBrick brick in bricks)
            {
                bagOfTetriminos.Add(brick);
            }
        }
    }
}
