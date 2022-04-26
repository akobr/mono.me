namespace c0ded0c.Core
{
    public interface IStoreModel
    {
        string Hash { get; }

        string Path { get; }

        string FullName { get; }

        string Name { get; }

        IExpansion? Expansion { get; }
    }
}
