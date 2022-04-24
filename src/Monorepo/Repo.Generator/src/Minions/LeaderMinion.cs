using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using _42.Monorepo.Repo.Generator.Strategies;
using Bogus;

namespace _42.Monorepo.Repo.Generator.Minions;

internal class LeaderMinion
{
    private readonly Simulation _simulation;
    private readonly Faker _faker = new();
    private readonly HashSet<string> _usedWorksteads = new();

    public LeaderMinion(Simulation simulation)
    {
        _simulation = simulation;
    }

    public void Work()
    {
        while (true)
        {
            try
            {
                if (_simulation.WorkItemsInQueue < _simulation.NumberOfDevelopers * 2)
                {
                    var workItemsInQueue = _simulation.WorkItemsInQueue;
                    var numberOfNewItems = (_simulation.NumberOfDevelopers * 3) - workItemsInQueue;

                    for (var i = 0; i < numberOfNewItems; i++)
                    {
                        CreateNewWorkItem();
                    }
                }

                Task.Delay(200, _simulation.Cancellation).Wait();

                if (_simulation.Status >= SimulationStatus.Cancelling
                    || _simulation.Cancellation.IsCancellationRequested)
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                if (exception.InnerException is TaskCanceledException)
                {
                    return;
                }

                Console.WriteLine($"!! Leader error: {exception.Message}");
                Console.WriteLine(exception.StackTrace);
            }
        }
    }

    private void CreateNewWorkItem()
    {
        var workIsFix = _faker.Random.Bool(0.82f);
        var availableWorksteads = GetAvailableWorksteads();

        var worksteadIndex = _faker.Random.Int(0, availableWorksteads.Count - 1);
        var workstead = availableWorksteads[worksteadIndex];
        _usedWorksteads.Add(workstead);

        if (!workIsFix)
        {
            if (_faker.Random.Bool(0.96f))
            {
                CreateFeatureWorkItem(workstead);
            }

            CreateNewWorksteadWorkItem();
        }

        CreateFixWorkItem(workstead);
    }

    private void CreateNewWorksteadWorkItem()
    {
        var work = new WorkOptions
        {
            BranchName = $"workstead/{_faker.Random.GetValidName()}",
            Work = new WorksteadStrategy(),
            Complexity = _faker.Random.Int(4, 10),
        };

        _simulation.AddWork(work);
    }

    private void CreateFeatureWorkItem(string workstead)
    {
        var work = new WorkOptions
        {
            Workstead = workstead,
            BranchName = $"feature/{_faker.Random.GetValidName()}",
            Work = new FeatureStrategy(),
            Complexity = _faker.Random.Int(2, 10),
        };

        _simulation.AddWork(work);
    }

    private void CreateFixWorkItem(string workstead)
    {
        var work = new WorkOptions
        {
            Workstead = workstead,
            BranchName = $"fix/{_faker.Random.GetValidName()}",
            Work = new FixStrategy(),
            Complexity = _faker.Random.Int(1, 10),
        };

        _simulation.AddWork(work);
    }

    private IReadOnlyList<string> GetAvailableWorksteads()
    {
        var allWorksteads = GetAllWorkstead();
        var availableWorksteads = allWorksteads;

        if (_usedWorksteads.Count >= allWorksteads.Count)
        {
            _usedWorksteads.Clear();
        }
        else
        {
            availableWorksteads = allWorksteads
                .Where(w => !_usedWorksteads.Contains(w))
                .ToList();
        }

        return availableWorksteads;
    }

    private IReadOnlyList<string> GetAllWorkstead()
    {
        var sourceFolder = new DirectoryInfo(Path.Combine(_simulation.RepoPath, "src"));
        return sourceFolder.GetDirectories().Select(d => d.Name).ToList();
    }
}
