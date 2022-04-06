using Semver;

namespace _42.Monorepo.Cli.Versioning;

public interface IVersionTemplate
{
    string Template { get; }

    SemVersion Version { get; }
}
