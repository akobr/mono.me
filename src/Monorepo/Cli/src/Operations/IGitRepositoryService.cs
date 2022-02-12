using LibGit2Sharp;

namespace _42.Monorepo.Cli.Operations
{
    public interface IGitRepositoryService
    {
        Repository BuildRepository();
    }
}
