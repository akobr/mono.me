namespace c0ded0c.Core
{
    public interface IArtifactContentBuilder
    {
        IContent Build<TObject>(TObject value)
            where TObject : class;
    }
}
