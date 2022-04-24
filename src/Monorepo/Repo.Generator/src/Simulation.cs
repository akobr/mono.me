using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using _42.Monorepo.Repo.Generator.Dependencies;
using LibGit2Sharp;

namespace _42.Monorepo.Repo.Generator
{
    internal class Simulation
    {
        private readonly object _lock = new();
        private readonly CancellationTokenSource cancellationSource = new();
        private readonly ConcurrentQueue<WorkOptions> _work = new();
        private readonly ConcurrentBag<string> _doneWork = new();
        private readonly DependenciesManager _dependencies = new();
        private readonly WorkStats _statistics = new();
        private string _repoPath;

        public SimulationStatus Status { get; private set; }

        public int NumberOfDevelopers { get; set; }

        public CancellationToken Cancellation => cancellationSource.Token;

        public int WorkItemsInQueue => _work.Count;

        public string RepoPath => _repoPath;

        public void Initialise(string initPath, int numberOfDevelopers)
        {
            _repoPath = initPath;
            _dependencies.Initialise(initPath);

            var repoStatsFilePath = Path.Combine(initPath, "repo-stats.json");
            LoadRepoStats(repoStatsFilePath);

            NumberOfDevelopers = numberOfDevelopers;
            Status = SimulationStatus.Running;
        }

        public void AddWorkResults(WorkStats workStatistics)
        {
            lock (_lock)
            {
                _statistics.Add(workStatistics);
            }
        }

        public void AddWorkResults(WorkResults workResults, WorkOptions workItem)
        {
            var workStatistics = workResults.Statistics;

            lock (_lock)
            {
                using var repo = new Repository(_repoPath);

                if (repo.Head.FriendlyName != "main")
                {
                    Commands.Checkout(repo, repo.Branches["main"]);
                }

                if (repo.RetrieveStatus().IsDirty)
                {
                    repo.Reset(ResetMode.Hard);
                }

                var workBranch = repo.Branches[workItem.BranchName];
                repo.Merge(workBranch);
                repo.Branches.Remove(workBranch);

                if (repo.RetrieveStatus().IsDirty)
                {
                    repo.Reset(ResetMode.Hard);
                    Console.WriteLine($"! {workItem.WorkType};{workItem.Complexity} skipped because contains collisions");
                    return;
                }

                // repo.Network.Push(repo.Head); // TODO: push new changes to remote (possibly each 10 work item or something)
                _statistics.Add(workStatistics);

                if (!string.IsNullOrEmpty(workResults.WorksteadName)
                    && workResults.NewProjects.Count > 0)
                {
                    var solutionFilePath = Path.Combine(_repoPath, "mono.sln");
                    var newFixedProjects = workResults.NewProjects.Select(FixRepoProjectFilePath).ToList();

                    var script = $"dotnet sln add -s {workResults.WorksteadName} {string.Join(' ', newFixedProjects)}";
                    Executor.ExecuteScriptAsync(script, _repoPath).Wait();
                    Commands.Stage(repo, solutionFilePath);
                    repo.Commit("chore: add new workstead to the solution file");

                    _dependencies.AddWorksteadToDependencyGraph(newFixedProjects);
                    _dependencies.SaveDependencyGraph();
                    Commands.Stage(repo, _dependencies.GraphFilePath);
                    repo.Commit("chore: add new workstead to dependency graph");

                    var projectSet = new HashSet<string>(newFixedProjects, StringComparer.OrdinalIgnoreCase);

                    foreach (var edge in _dependencies.Graph.Edges.Where(e => projectSet.Contains(e.Source)))
                    {
                        var addReferenceScript = $"dotnet add {edge.Source} reference {edge.Target}";
                        Executor.ExecuteScriptAsync(addReferenceScript, _repoPath).Wait();
                        Commands.Stage(repo, edge.Source);
                    }

                    repo.Commit("build: add project references in new workstead");
                }
            }

            _doneWork.Add($"{workItem.Workstead}; {workItem.Complexity}; ↑{workStatistics.CommitsCount} +{workStatistics.AddedFilesCount} ~{workStatistics.UpdatedFilesCount} -{workStatistics.DeletedFilesCount}");
        }

        public void AddWork(WorkOptions work)
        {
            _work.Enqueue(work);
        }

        public bool TryGetWorkFromQueue([MaybeNullWhen(false)] out WorkOptions work)
        {
            return _work.TryDequeue(out work);
        }

        public async Task StopAsync(string outputPath)
        {
            if (Status != SimulationStatus.Running)
            {
                Status = SimulationStatus.Cancelled;
                return;
            }

            Status = SimulationStatus.Cancelling;
            cancellationSource.Cancel();

            try
            {
                var repoStatsFilePath = Path.Combine(outputPath, "repo-stats.json");
                SaveRepoStats(repoStatsFilePath);

                var workLogFilePath = Path.Combine(outputPath, "work.json");
                await File.AppendAllLinesAsync(workLogFilePath, _doneWork, default); 
            }
            finally
            {
                Status = SimulationStatus.Cancelled;
            }
        }

        private string FixRepoProjectFilePath(string workingFilePath)
        {
            var indexOfSrcFolder = workingFilePath.IndexOf("\\src\\") + 1;
            return Path.Combine(_repoPath, workingFilePath[indexOfSrcFolder..]);
        }

        private void LoadRepoStats(string repoStatsFilePath)
        {
            if (!File.Exists(repoStatsFilePath))
            {
                return;
            }

            var fileContent = File.ReadAllText(repoStatsFilePath);
            var stats = JsonSerializer.Deserialize<WorkStats>(fileContent);

            if (stats is not null)
            {
                AddWorkResults(stats);
            }
        }

        private void SaveRepoStats(string repoStatsFilePath)
        {
            try
            {
                lock (_lock)
                {
                    using var repoStatsSteam = new FileStream(repoStatsFilePath, FileMode.Create, FileAccess.Write);
                    using var writer = new Utf8JsonWriter(repoStatsSteam);
                    JsonSerializer.Serialize(writer, _statistics, new JsonSerializerOptions { WriteIndented = true });
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Error on exit " + e.Message);
            }
        }
    }
}
