using System;
using System.IO;
using System.Threading.Tasks;
using LibGit2Sharp;

namespace _42.Monorepo.Repo.Generator.Minions;

internal class DeveloperMinion
{
    private readonly Simulation _simulation;
    private readonly int _index;

    public DeveloperMinion(Simulation simulation, int index)
    {
        _simulation = simulation;
        _index = index;
    }

    public void Work(object? parameter)
    {
        if (parameter is not string workPlacePath)
        {
            return;
        }

        while (true)
        {
            try
            {
                Task.Delay(200).Wait(_simulation.Cancellation);

                if (_simulation.Status >= SimulationStatus.Cancelling)
                {
                    break;
                }

                if (_simulation.Status != SimulationStatus.Running
                    || !_simulation.TryGetWorkFromQueue(out var work))
                {
                    continue;
                }

                work.RepositoryPath = workPlacePath;
                DoWorkAsync(work).Wait(_simulation.Cancellation);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                Console.WriteLine($"!! Developer error: {exception.Message}");
                Console.WriteLine(exception.StackTrace);
            }
        }
    }

    private async Task DoWorkAsync(WorkOptions options)
    {
        Console.WriteLine($"{_index}: {options.WorkType};{options.Complexity}");
        using var repo = new Repository(options.RepositoryPath);

        if (repo.Head.FriendlyName != "main")
        {
            Commands.Checkout(repo, repo.Branches["main"]);
        }

        var gitConfig = repo.Config;
        var signature = gitConfig.BuildSignature(DateTimeOffset.Now);
        Commands.Pull(repo, signature, new PullOptions());
        var branch = repo.CreateBranch(options.BranchName);
        Commands.Checkout(repo, branch);

        try
        {
            // Load all projects in entire mono-repo
            var solutionFilePath = Path.Combine(options.RepositoryPath, "mono.sln");
            using var workspace = new MsBuild(solutionFilePath);
            await workspace.InitialiseAsync();

            var results = await options.Work.DoWorkAsync(repo, workspace.Projects, options);

            repo.Branches.Update(branch, (updater) =>
            {
                updater.Remote = "origin";
                updater.UpstreamBranch = branch.CanonicalName;
            });

            // repo.Network.Push(branch, new PushOptions()); // local push doesn't (yet) support pushing to non-bare repos
            await Executor.ExecuteScriptAsync("git push", options.RepositoryPath);
            _simulation.AddWorkResults(results, options);
        }
        catch (Exception exception)
        {
            Console.WriteLine($"!! {exception.Message}");
        }
        finally
        {
            if (repo.RetrieveStatus().IsDirty)
            {
                repo.Reset(ResetMode.Hard);
            }

            Commands.Checkout(repo, repo.Branches["main"]);
            repo.Branches.Remove(branch);
        }
    }
}
