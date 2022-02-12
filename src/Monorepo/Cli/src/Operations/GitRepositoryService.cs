using System;
using LibGit2Sharp;

namespace _42.Monorepo.Cli.Operations
{
    public class GitRepositoryService : IGitRepositoryService
    {
        private readonly Lazy<string> repositoryPath;

        public GitRepositoryService()
        {
            repositoryPath = new Lazy<string>(MonorepoDirectoryFunctions.GetMonorepoRootDirectory);
        }

        public Repository BuildRepository()
        {
            return new Repository(repositoryPath.Value);
        }
    }
}
