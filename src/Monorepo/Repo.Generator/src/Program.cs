using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Repo.Generator.Dependencies;
using _42.Monorepo.Repo.Generator.Minions;
using _42.Monorepo.Repo.Generator.Strategies;
using _42.Monorepo.Repo.Generator.Templates;
using Bogus;
using LibGit2Sharp;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using QuikGraph;

namespace _42.Monorepo.Repo.Generator
{
    internal static class Program
    {
        private static readonly Simulation Simulation = new();
        private static readonly LeaderMinion Leader = new(Simulation);
        private static string RepoPath = string.Empty;
        private static List<Thread> workers = new();

        public static async Task Main(string[] args)
        {
            // This loads the real MSBuild from the toolset so that all targets and SDKs can be found as if a real build is happening
            MSBuildLocator.RegisterDefaults();

            var repoPath = RepoPath = args[0];
            var workDirectory = args[1];
            var numberOfWorkers = int.Parse(args[2]);

            Console.WriteLine($"Main repo in:      {repoPath}");
            Console.WriteLine($"Working in:        {workDirectory}");
            Console.WriteLine($"Number of workers: {numberOfWorkers}");

            if (!Repository.IsValid(repoPath))
            {
                await InitializeRepositoryAsync(repoPath, 5 * numberOfWorkers);
            }

            Simulation.Initialise(repoPath, numberOfWorkers);
            var workPlaces = PrepareWorkingPlaces(repoPath, workDirectory, numberOfWorkers);
            StartWorkers(workPlaces);

            Console.CancelKeyPress += OnCancellationRequested;
            await WaitTillEndAsync();
        }

        private static async void OnCancellationRequested(object? sender, ConsoleCancelEventArgs e)
        {
            await Simulation.StopAsync(RepoPath);
        }

        private static async Task WaitTillEndAsync()
        {
            while (Simulation.Status != SimulationStatus.Cancelled)
            {
                await Task.Delay(500);
            }

            await Task.Delay(1000);
        }

        private static void StartWorkers(IEnumerable<string> workPlaces)
        {
            var leaderThread = new Thread(Leader.Work);
            leaderThread.Start();
            workers.Add(leaderThread);
            var developerIndex = 0;

            foreach (var workPlace in workPlaces)
            {
                var worker = new DeveloperMinion(Simulation, ++developerIndex);
                var workerThread = new Thread(worker.Work);
                workerThread.Start(workPlace);
                workers.Add(workerThread);
            }
        }

        private static async Task InitializeRepositoryAsync(string repoPath, int numberOfWorksteads)
        {
            var initStats = new WorkStats();
            Repository.Init(repoPath);
            using var repo = new Repository(repoPath);
            var dependencyGraph = new BidirectionalGraph<string, IEdge<string>>();
            dependencyGraph.AddVertex("root");

            var ignoreFilePath = Path.Combine(repoPath, ".gitignore");
            var ignoreFile = new DotGitIgnoreT4();
            await File.WriteAllTextAsync(ignoreFilePath, ignoreFile.TransformText());
            Commands.Stage(repo, ignoreFilePath);
            repo.Commit("chore: repo initialization");

            var mainBranch = repo.CreateBranch("main");
            repo.Config.Add("init.defaultBranch", "main");

            var initBranchName = "feature/initialization";
            var initBranch = repo.CreateBranch(initBranchName);
            Commands.Checkout(repo, initBranch);

            var worksteadCreateStrategy = new WorksteadStrategy();

            await Executor.ExecuteScriptAsync("dotnet new sln -n mono", repoPath);

            var results = await worksteadCreateStrategy.DoWorkAsync(
                repo,
                Array.Empty<Project>(),
                new WorkOptions { RepositoryPath = repoPath, BranchName = initBranchName, Complexity = 10 });
            initStats.Add(results.Statistics);

            var componentsListFilePath = Path.Combine(repoPath, "components.list");
            await File.WriteAllLinesAsync(componentsListFilePath, results.NewProjects);
            Commands.Stage(repo, componentsListFilePath);
            repo.Commit("chore: add components project list");
            dependencyGraph.AddVertexRange(results.NewProjects);

            var scriptAddComponentsProjects = $"dotnet sln add -s Components {string.Join(' ', results.NewProjects)}";
            await Executor.ExecuteScriptAsync(scriptAddComponentsProjects, repoPath);

            var graphFilePath = Path.Combine(repoPath, "dependency-graph.xml");
            var dependencyManager = new DependenciesManager(dependencyGraph, graphFilePath, results.NewProjects);

            for (var i = 1; i < numberOfWorksteads; i++)
            {
                results = await worksteadCreateStrategy.DoWorkAsync(
                    repo,
                    Array.Empty<Project>(),
                    new WorkOptions { RepositoryPath = repoPath, BranchName = initBranchName, Complexity = 3 });

                initStats.Add(results.Statistics);
                var script = $"dotnet sln add -s {results.WorksteadName} {string.Join(' ', results.NewProjects)}";
                await Executor.ExecuteScriptAsync(script, repoPath);
                dependencyManager.AddWorksteadToDependencyGraph(results.NewProjects);
            }

            var solutionFilePath = Path.Combine(repoPath, "mono.sln");
            Commands.Stage(repo, solutionFilePath);
            repo.Commit("chore: add solution file");

            dependencyManager.SaveDependencyGraph();
            Commands.Stage(repo, dependencyManager.GraphFilePath);
            repo.Commit("chore: add dependency graph");

            var modifiedProjectFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var edge in dependencyManager.Graph.Edges)
            {
                if (edge.Source == "root")
                {
                    continue;
                }

                var addReferenceScript = $"dotnet add {edge.Source} reference {edge.Target}";
                await Executor.ExecuteScriptAsync(addReferenceScript, repoPath);
                modifiedProjectFiles.Add(edge.Source);
            }

            Commands.Stage(repo, modifiedProjectFiles);
            repo.Commit("build: add project references");

            Commands.Checkout(repo, mainBranch);
            repo.Merge(initBranch);

            initStats.AddedFilesCount += 4;
            initStats.CommitsCount += 4;
            Simulation.AddWorkResults(initStats);
        }

        private static IEnumerable<string> PrepareWorkingPlaces(string repoPath, string workDirectory, int numberOfWorkers)
        {
            var places = new List<string>(numberOfWorkers);

            for (var i = 0; i < numberOfWorkers; i++)
            {
                var workRepoPath = Path.Combine(workDirectory, Path.GetRandomFileName());
                places.Add(workRepoPath);

                Repository.Clone(repoPath, workRepoPath);
                using var workRepo = new Repository(workRepoPath);

                var faker = new Faker();
                workRepo.Config.Add("user.name", faker.Person.FullName);
                workRepo.Config.Add("user.email", faker.Person.Email);
            }

            return places;
        }
    }
}
