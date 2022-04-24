using System.Collections.Generic;
using System.Threading.Tasks;
using LibGit2Sharp;
using Microsoft.CodeAnalysis;

namespace _42.Monorepo.Repo.Generator
{
    internal interface IWorkStrategy
    {
        Task<WorkResults> DoWorkAsync(IRepository repo, IReadOnlyList<Project> assemblies, WorkOptions options);
    }
}
