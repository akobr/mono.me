using Semver;

namespace _42.Monorepo.Cli.Model
{
    public interface IExternalDependency
    {
        string Name { get; }

        SemVersion Version { get; }
    }
}
