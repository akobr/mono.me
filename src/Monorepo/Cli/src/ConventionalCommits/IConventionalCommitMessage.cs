namespace _42.Monorepo.Cli.ConventionalCommits
{
    public interface IConventionalCommitMessage
    {
        string Type { get; }

        string Description { get; }

        bool IsBreakingChange { get; }

        string? Scope { get; }

        string? IssueLink { get; }
    }
}
