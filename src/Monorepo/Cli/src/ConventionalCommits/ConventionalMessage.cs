using System.Text;

namespace _42.Monorepo.Cli.ConventionalCommits
{
    public class ConventionalMessage : IConventionalMessage
    {
        public ConventionalMessage(
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

        public string GetFullRepresentation()
        {
            var builder = new StringBuilder();
            builder.Append(Type);

            if (!string.IsNullOrEmpty(Scope))
            {
                builder.Append(" (");
                builder.Append(Scope);
                builder.Append(')');
            }

            if (IsBreakingChange)
            {
                builder.Append('!');
            }

            builder.Append(": ");

            if (!string.IsNullOrEmpty(IssueLink))
            {
                builder.Append(IssueLink);
                builder.Append(' ');
            }

            builder.Append(Description);
            return builder.ToString();
        }

        public string GetSimpleRepresentation()
        {
            var builder = new StringBuilder();
            builder.Append(Type);
            builder.Append(": ");
            builder.Append(Description);

            if (!string.IsNullOrEmpty(IssueLink))
            {
                builder.Append(" (");
                builder.Append(IssueLink);
                builder.Append(')');
            }

            return builder.ToString();
        }
    }
}
