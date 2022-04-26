namespace c0ded0c.Core
{
    public interface IArtifactInfo
    {
        string Key { get; }

        string Path { get; }

        ContentType Type { get; }

        IContent Content { get; }

        bool HasContent { get; }

        void SetContent(IContent content);
    }
}
