using LibGit2Sharp;

namespace _42.Monorepo.Cli.Operations
{
    public interface IGitRepositoryFactory
    {
        LibGit2Sharp.Repository BuildRepository();
    }
}
