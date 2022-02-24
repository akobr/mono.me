using System.Collections.Generic;
using System.Linq;
using _42.Monorepo.Cli.Configuration;
using _42.Monorepo.Cli.ConventionalCommits;
using _42.Monorepo.Cli.Extensions;
using _42.Monorepo.Cli.Operations;
using LibGit2Sharp;
using Microsoft.Extensions.Options;

namespace _42.Monorepo.Cli.Git
{
    public class GitHistoryService : IGitHistoryService
    {
        private const int DefaultDeepFromStopCommits = 100;
        private const int MaxDeepWithUnknownChanges = 1000;
        private const int MaxUnknownChangesInReport = 200;

        private readonly ReleaseOptions _options;
        private readonly IGitRepositoryService _repositoryService;

        public GitHistoryService(
            IGitRepositoryService repositoryService,
            IOptions<ReleaseOptions> configuration)
        {
            _options = configuration.Value;
            _repositoryService = repositoryService;
        }

        public GitHistoryReport GetHistory(string targetedRepoRelativePath, IReadOnlyCollection<Commit> commitsToStop)
        {
            using var repo = _repositoryService.BuildRepository();

            var stopCommits = CreateStopSet(commitsToStop);
            var visitedCommits = new HashSet<ObjectId>();
            var parser = new ConventionalParser();
            var changes = new List<(Commit, IConventionalMessage)>();
            var unknownCommits = new List<Commit>();
            var unknownDeep = 0;
            var knownChangeTypeSet = new HashSet<string>(
                _options.Changes.Major
                    .Concat(_options.Changes.Minor)
                    .Concat(_options.Changes.Patch)
                    .Concat(_options.Changes.Harmless));

            var toProcess = new Stack<Commit>();
            toProcess.Push(repo.Head.Tip);

            while (toProcess.Count > 0)
            {
                var commit = toProcess.Pop();

                // stop at any release or already visited commit
                if (stopCommits.Contains(commit.Id)
                    || visitedCommits.Contains(commit.Id))
                {
                    continue;
                }

                // flag as visited
                visitedCommits.Add(commit.Id);

                // too deep with unknown changes
                if (unknownDeep > MaxDeepWithUnknownChanges)
                {
                    continue;
                }

                // register parents for processing
                foreach (var parent in commit.Parents)
                {
                    toProcess.Push(parent);
                }

                // detect if the commit is relevant (there are some changes)
                if (!commit.IsRelevantCommit(repo, targetedRepoRelativePath))
                {
                    continue;
                }

                if (!parser.TryParseCommitMessage(commit.MessageShort, out var conventionalMessage))
                {
                    if (++unknownDeep > MaxUnknownChangesInReport)
                    {
                        continue;
                    }

                    // as unknown change
                    unknownCommits.Add(commit);
                }
                else
                {
                    // stop at any previous release commit
                    // TODO: [P2] what about reverted release commits
                    if (conventionalMessage.Type is "release" or ":bookmark:")
                    {
                        continue;
                    }

                    // store changes (unknown or known)
                    if (knownChangeTypeSet.Contains(conventionalMessage.Type))
                    {
                        changes.Add((commit, conventionalMessage));
                    }
                    else
                    {
                        unknownCommits.Add(commit);
                    }
                }
            }

            return new GitHistoryReport(changes, unknownCommits);
        }

        private ISet<ObjectId> CreateStopSet(IReadOnlyCollection<Commit> commitsToStop)
        {
            var stopCommits = new HashSet<ObjectId>();
            var toProcess = new Stack<(Commit Commit, int Deep)>(commitsToStop.Select(c => (c, 0)));

            while (toProcess.Count > 0)
            {
                var pair = toProcess.Pop();

                if (!stopCommits.Add(pair.Commit.Id)
                    || pair.Deep >= DefaultDeepFromStopCommits)
                {
                    continue;
                }

                var nextDeep = pair.Deep + 1;
                foreach (var parentCommit in pair.Commit.Parents)
                {
                    toProcess.Push((parentCommit, nextDeep));
                }
            }

            return stopCommits;
        }
    }

}
