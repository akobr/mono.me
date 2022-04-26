namespace c0ded0c.Core
{
    public class JsonArtifactContentBuilder : IArtifactContentBuilder
    {
        public IContent Build<TObject>(TObject value)
            where TObject : class
        {
            return new JsonContent<TObject>(value);
        }
    }
}
