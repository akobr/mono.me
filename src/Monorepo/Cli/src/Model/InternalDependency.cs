namespace _42.Monorepo.Cli.Model
{
    public class InternalDependency : IInternalDependency
    {
        public InternalDependency(string name, string path)
        {
            Name = name;
            Path = path;
        }

        public string Name { get; }

        public string Path { get; }
    }
}
