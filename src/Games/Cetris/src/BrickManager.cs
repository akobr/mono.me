using System;
using System.Collections.Generic;

namespace _42.Cetris;

public class BrickManager
{
    private static readonly IBrick[] Bricks =
    {
        new Brick(
            BrickType.O,
            (byte)ConsoleColor.Red,
            0b_0000_0110_0110_0000),
        new Brick(
            BrickType.I,
            (byte)ConsoleColor.Green,
            0b_0000_1111_0000_0000,
            0b_0010_0010_0010_0010),
        new Brick(
            BrickType.S,
            (byte)ConsoleColor.Yellow,
            0b_0000_0011_0110_0000,
            0b_0100_0110_0010_0000),
        new Brick(
            BrickType.Z,
            (byte)ConsoleColor.Cyan,
            0b_0000_0110_0011_0000,
            0b_0010_0110_0100_0000),
        new Brick(
            BrickType.T,
            (byte)ConsoleColor.Magenta,
            0b_0000_0010_0111_0000,
            0b_0001_0011_0001_0000,
            0b_0111_0010_0000_0000,
            0b_0100_0110_0100_0000),
        new Brick(
            BrickType.L,
            (byte)ConsoleColor.Blue,
            0b_0100_0100_0110_0000,
            0b_0000_0001_0111_0000,
            0b_0110_0010_0010_0000,
            0b_0000_0111_0100_0000),
        new Brick(
            BrickType.J,
            (byte)ConsoleColor.Gray,
            0b_0000_0100_0111_0000,
            0b_0010_0010_0110_0000,
            0b_0000_0111_0001_0000,
            0b_0110_0100_0100_0000),
    };

    private readonly Random _random;
    private readonly HashSet<IBrick> _bagOfTetriminos;
    private readonly Queue<IBrick> _lastDraws;

    public BrickManager()
    {
        _random = new Random();
        _bagOfTetriminos = new HashSet<IBrick>(Bricks.Length);
        _lastDraws = new Queue<IBrick>(Bricks.Length);
        InitialiseBag();
    }

    public IReadOnlyList<IBrick> AllBricks => Bricks;

    public IBrick GetRandomBrick()
    {
        // TODO
        return Bricks[_random.Next(0, Bricks.Length)];
    }

    public IBrick GetBrick(BrickType type)
    {
        return Bricks[(int)type];
    }

    private void InitialiseBag()
    {
        foreach (var brick in Bricks)
        {
            _bagOfTetriminos.Add(brick);
        }
    }
}
