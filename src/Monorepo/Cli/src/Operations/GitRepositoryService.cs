using System;
using LibGit2Sharp;

namespace _42.Monorepo.Cli.Operations
{
    public class GitRepositoryService : IGitRepositoryService
    {
        private readonly Lazy<string> _repositoryPath;

        public GitRepositoryService()
        {
            _repositoryPath = new Lazy<string>(MonorepoDirectoryFunctions.GetMonorepoRootDirectory);
        }

        public Repository BuildRepository()
        {
            return new Repository(_repositoryPath.Value);
        }
    }
}
