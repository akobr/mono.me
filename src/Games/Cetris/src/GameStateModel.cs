namespace _42.Cetris;

public class GameStateModel
{
    public GameInfo Info { get; set; }

    public BrickModel CurrentBrick { get; set; }

    public BrickModel NextBrick { get; set; }

    public byte[] Bytes { get; set; }

    public byte[] Colors { get; set; }

    public class BrickModel
    {
        public BrickType Type { get; set; }

        public int StateIndex { get; set; }

        public PointModel? Position { get; set; }
    }

    public class PointModel
    {
        public int Row { get; set; }

        public int Column { get; set; }
    }
}
