using System.Collections.Generic;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.CodeAnalysis;

namespace _42.Monorepo.Repo.Generator.Strategies
{
    internal class EmptyStrategy : IWorkStrategy
    {
        public Task<WorkResults> DoWorkAsync(IRepository repo, IReadOnlyList<Project> projects, WorkOptions options)
        {
            return Task.FromResult(new WorkResults());
        }
    }
}
