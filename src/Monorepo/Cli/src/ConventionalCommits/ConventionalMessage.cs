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
            var simpleText = new StringBuilder();
            simpleText.Append(Type);

            if (!string.IsNullOrEmpty(Scope))
            {
                simpleText.Append(" (");
                simpleText.Append(Scope);
                simpleText.Append(')');
            }

            simpleText.Append(": ");

            if (!string.IsNullOrEmpty(IssueLink))
            {
                simpleText.Append(IssueLink);
                simpleText.Append(' ');
            }

            simpleText.Append(Description);
            return simpleText.ToString();
        }

        public string GetSimpleRepresentation()
        {
            var simpleText = new StringBuilder();
            simpleText.Append(Type);
            simpleText.Append(": ");
            simpleText.Append(Description);

            if (!string.IsNullOrEmpty(IssueLink))
            {
                simpleText.Append(" (");
                simpleText.Append(IssueLink);
                simpleText.Append(')');
            }

            return simpleText.ToString();
        }
    }
}
