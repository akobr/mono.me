using System.Collections.Generic;

namespace _42.Monorepo.Cli.ConventionalCommits
{
    public interface IConventionalMessage
    {
        string Type { get; }

        string Description { get; }

        bool IsBreakingChange { get; }

        string? Scope { get; }

        string? IssueLink { get; }

        public IReadOnlyCollection<string> Links { get; }

        string GetFullRepresentation();

        string GetSimpleRepresentation();
    }
}
