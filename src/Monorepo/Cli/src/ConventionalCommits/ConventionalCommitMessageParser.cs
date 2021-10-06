using System.Text.RegularExpressions;

namespace _42.Monorepo.Cli.ConventionalCommits
{
    public class ConventionalCommitMessageParser
    {
        private const string TEMPLATE =
            @"^(?<type>[\w]+)(\((?<scope>[\w\-]+)\))?(?<breaking>!)?: ?((?<issue>([A-Z]+-|#)[\d]+) )?(?<description>.*)";

        private readonly Regex regex;

        public ConventionalCommitMessageParser()
        {
            regex = new Regex(TEMPLATE, RegexOptions.Compiled);
        }

        public bool TryParseCommitMessage(string message, out IConventionalCommitMessage conventionalMessage)
        {
            var match = regex.Match(message);

            if (!match.Success)
            {
                conventionalMessage = new ConventionalCommitMessage(string.Empty, string.Empty, false);
                return false;
            }

            conventionalMessage = new ConventionalCommitMessage(
                match.Groups["type"].Value,
                match.Groups["description"].Value,
                match.Groups.ContainsKey("breaking"))
            {
                Scope = match.Groups.ContainsKey("scope") ? match.Groups["scope"].Value : null,
                IssueLink = match.Groups.ContainsKey("issue") ? match.Groups["issue"].Value : null,
            };
            return true;
        }
    }
}
