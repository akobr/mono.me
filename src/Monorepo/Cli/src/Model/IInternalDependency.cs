namespace _42.Monorepo.Cli.Model
{
    public interface IInternalDependency
    {
        string Name { get; }

        string FullPath { get; }

        string RepoRelativePath { get; }
    }
}
