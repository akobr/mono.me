namespace _42.Monorepo.Cli.ConventionalCommits
{
    public interface IConventionalMessage
    {
        string Type { get; }

        string Description { get; }

        bool IsBreakingChange { get; }

        string? Scope { get; }

        string? IssueLink { get; }

        string GetFullRepresentation();

        string GetSimpleRepresentation();
    }
}
