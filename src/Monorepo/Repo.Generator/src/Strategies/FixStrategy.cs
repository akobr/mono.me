using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bogus;
using LibGit2Sharp;
using Microsoft.CodeAnalysis;

namespace _42.Monorepo.Repo.Generator.Strategies
{
    internal class FixStrategy : IWorkStrategy
    {
        private static readonly Faker _faker = new();

        public async Task<WorkResults> DoWorkAsync(IRepository repo, IReadOnlyList<Project> projects, WorkOptions options)
        {
            var results = new WorkResults();
            var modifiedFiles = new HashSet<string>();
            var targetProjectFilePathPrefix = Path.Combine(options.RepositoryPath, "src", options.Workstead);
            var targetProjects = projects.Where(p => p.FilePath is not null && p.FilePath.StartsWith(targetProjectFilePathPrefix)).ToList();

            for (var i = 0; i < options.Complexity; i++)
            {
                var projectIndex = _faker.Random.Int(0, targetProjects.Count - 1);
                var project = targetProjects[projectIndex];
                var projectName = project.Name;
                var numberOfChanges = Math.Max(options.Complexity / 3, 1) + 1;
                var documents = project.Documents.Where(d => !string.IsNullOrEmpty(d.FilePath) && !d.FilePath.Contains("\\obj\\")).ToList();
                var filesToCommit = new HashSet<string>();

                for (var ch = 0; ch < numberOfChanges; ch++)
                {
                    var documentIndex = _faker.Random.Int(0, documents.Count - 1);
                    var document = documents[documentIndex];
                    var syntaxRoot = await document.GetSyntaxRootAsync();

                    if (syntaxRoot is null
                        || string.IsNullOrEmpty(document.FilePath))
                    {
                        continue;
                    }

                    var methodGenerator = new ClassMethodGenerator();
                    var result = methodGenerator.Visit(syntaxRoot);

                    if (!syntaxRoot.IsEquivalentTo(result))
                    {
                        filesToCommit.Add(document.FilePath);
                        modifiedFiles.Add(document.FilePath);

                        results.Statistics.NumberOfCodeLines += 4;
                        var normalizedResult = result.NormalizeWhitespace();
                        await File.WriteAllTextAsync(document.FilePath, normalizedResult.ToFullString());
                    }

                     // TODO: [P1] add reference, using and usage of new method
                    // var ancestors = options.DependencyGraph.GetAllAncestors(projectName).ToList();
                    // var ancestorIndex = _faker.Random.Int(0, ancestors.Count - 1);
                    // var ancestorRepoPath = ancestors[ancestorIndex];
                    // var ancestor = projects.First(p => p.FilePath == Path.Combine(options.RepositoryPath, ancestorRepoPath));
                }

                if (filesToCommit.Count > 0)
                {
                    Commands.Stage(repo, filesToCommit);
                    var signature = repo.Config.BuildSignature(DateTimeOffset.Now);
                    repo.Commit($"fix: {string.Join(' ', _faker.Lorem.Words(_faker.Random.Int(2, 7)))}", signature, signature);
                    results.Statistics.CommitsCount++;
                }
            }

            results.Statistics.UpdatedFilesCount = modifiedFiles.Count;
            return results;
        }
    }
}
