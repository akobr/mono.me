using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using _42.Monorepo.Repo.Generator.Templates;
using Bogus;
using Humanizer;
using LibGit2Sharp;
using Microsoft.CodeAnalysis;
using ProjectCsprojT4 = _42.Monorepo.Repo.Generator.Templates.ProjectCsprojT4;

namespace _42.Monorepo.Repo.Generator.Strategies
{
    internal class WorksteadStrategy : IWorkStrategy
    {
        private static readonly Faker _faker = new();

        public async Task<WorkResults> DoWorkAsync(IRepository repo, IReadOnlyList<Project> projects, WorkOptions options)
        {
            var gitConfig = repo.Config;
            var workstead = GetItemName();
            var worksteadDirectory = Path.Combine(options.RepositoryPath, "src", workstead);

            Directory.CreateDirectory(worksteadDirectory);
            await CreateReadmeFileAsync(repo, worksteadDirectory, workstead);

            var documentationDirectory = Path.Combine(options.RepositoryPath, "docs", workstead);
            Directory.CreateDirectory(documentationDirectory);
            await CreateReadmeFileAsync(repo, documentationDirectory, workstead);

            var worksteadSignature = gitConfig.BuildSignature(DateTimeOffset.Now);
            repo.Commit($"chore: create new workstead {workstead}", worksteadSignature, worksteadSignature);

            var projectsCount = Math.Min(Math.Max(1, options.Complexity), 10);
            var projectFilePaths = new List<string>();
            var newFilesTotalCount = 2;
            var numberOfCodeLines = 0;

            for (var i = 0; i < projectsCount; i++)
            {
                var projectName = GetItemName();
                var projectDirectory = Path.Combine(worksteadDirectory, projectName, "src");
                Directory.CreateDirectory(projectDirectory);

                var projectFilePath = Path.Combine(projectDirectory, $"{projectName}.csproj");
                projectFilePaths.Add(projectFilePath);
                var projectNamespaceFilePath = Path.Combine(projectDirectory, "namespace.txt");
                var projectNamespace = $"{workstead}.{projectName}";
                var projectTemplate = new ProjectCsprojT4(new ProjectCsprojModel
                {
                    AssemblyName = projectNamespace,
                    RootNamespace = projectNamespace,
                    HasCustomName = true,
                });

                await File.WriteAllTextAsync(projectFilePath, projectTemplate.TransformText());
                Commands.Stage(repo, projectFilePath);

                await File.WriteAllTextAsync(projectNamespaceFilePath, projectNamespace);
                Commands.Stage(repo, projectNamespaceFilePath);

                var classesCount = _faker.Random.Int(4, 12);
                newFilesTotalCount += classesCount + 2;
                numberOfCodeLines += classesCount * 4;

                for (var f = 0; f < classesCount; f++)
                {
                    var className = _faker.Random.GetValidName();
                    var classFilePath = Path.Combine(projectDirectory, $"{className}.cs");
                    var classContent = new StaticClassT4(new StaticClassModel
                    {
                        ClassName = className,
                        Namespace = projectNamespace,
                    });

                    await File.WriteAllTextAsync(classFilePath, classContent.TransformText());
                    Commands.Stage(repo, classFilePath);
                }

                repo.Commit($"chore: create new project {projectName}");
            }

            return new WorkResults
            {
                Statistics = new WorkStats
                {
                    WorksteadsCount = 1,
                    ProjectsCount = projectsCount,
                    CommitsCount = projectsCount + 1,
                    AddedFilesCount = newFilesTotalCount,
                    NumberOfCodeLines = numberOfCodeLines,
                },
                NewProjects = projectFilePaths,
                WorksteadName = workstead,
            };
        }

        private async Task CreateReadmeFileAsync(IRepository repo, string directoryPath, string title)
        {
            var readmeFilePath = Path.Combine(directoryPath, "README.md");
            var readmeContent = new StringBuilder($"# {title}\n\n");
            readmeContent.AppendLine(_faker.Lorem.Paragraphs(_faker.Random.Int(3, 6)));
            await File.WriteAllTextAsync(readmeFilePath, readmeContent.ToString());
            Commands.Stage(repo, readmeFilePath);
        }

        private string GetItemName()
        {
            return _faker.Commerce.ProductName().Humanize(LetterCasing.Title).Replace(' ', '.');
        }
    }
}
