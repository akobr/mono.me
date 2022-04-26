namespace c0ded0c.Core.Yaml
{
    public class YamlArtifactContentBuilder : IArtifactContentBuilder
    {
        public IContent Build<TObject>(TObject value)
            where TObject : class
        {
            return new YamlContent<TObject>(value);
        }
    }
}
