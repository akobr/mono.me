namespace _42.Monorepo.Cli.ConventionalCommits
{
    public class ConventionalCommitMessage : IConventionalCommitMessage
    {
        public ConventionalCommitMessage(
            string type,
            string description,
            bool isBreakingChange = false)
        {
            Type = type;
            Description = description;
            IsBreakingChange = isBreakingChange;
        }

        public string Type { get; init; }

        public string Description { get; init; }

        public bool IsBreakingChange { get; init; }

        public string? Scope { get; init; }

        public string? IssueLink { get; init; }
    }
}
