namespace _42.Monorepo.Cli.Model
{
    public class InternalDependency : IInternalDependency
    {
        public InternalDependency(string name, string repoRelativePath, string fullPath)
        {
            Name = name;
            FullPath = fullPath;
            RepoRelativePath = repoRelativePath;
        }

        public string Name { get; }

        public string FullPath { get; }

        public string RepoRelativePath { get; }
    }
}
