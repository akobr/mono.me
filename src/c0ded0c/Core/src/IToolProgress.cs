namespace c0ded0c.Core
{
    public interface IToolProgress
    {
        public double? Percentages { get; }

        public string? Category { get; }

        public string Message { get; }
    }
}
