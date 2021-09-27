using System;
using LibGit2Sharp;

namespace _42.Monorepo.Cli.Operations
{
    public class GitRepositoryFactory : IGitRepositoryFactory
    {
        private readonly Lazy<string> repositoryPath;

        public GitRepositoryFactory()
        {
            repositoryPath = new Lazy<string>(MonorepoDirectoryFunctions.GetMonorepoRootDirectory);
        }

        public LibGit2Sharp.Repository BuildRepository()
        {
            return new LibGit2Sharp.Repository(repositoryPath.Value);
        }
    }
}
