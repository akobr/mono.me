using System.Text.RegularExpressions;

namespace _42.Monorepo.Cli.ConventionalCommits
{
    public class ConventionalParser
    {
        private const string TEMPLATE =
            @"^(?<type>\:?[\w]+\:?)(\((?<scope>[\w\-]+)\))?(?<breaking>!)?: ?((?<issue>([A-Z]+-|#)[\da-z]+) )?(?<description>.*)";

        private readonly Regex regex;

        public ConventionalParser()
        {
            regex = new Regex(TEMPLATE, RegexOptions.Compiled);
        }

        public bool TryParseCommitMessage(string message, out IConventionalMessage conventionalMessage)
        {
            var match = regex.Match(message);

            if (!match.Success)
            {
                conventionalMessage = new ConventionalMessage(string.Empty, string.Empty, false);
                return false;
            }

            conventionalMessage = new ConventionalMessage(
                match.Groups["type"].Value,
                match.Groups["description"].Value,
                match.Groups["breaking"].Success)
            {
                Scope = match.Groups.ContainsKey("scope") ? match.Groups["scope"].Value : null,
                IssueLink = match.Groups.ContainsKey("issue") ? match.Groups["issue"].Value : null,
            };
            return true;
        }
    }
}
