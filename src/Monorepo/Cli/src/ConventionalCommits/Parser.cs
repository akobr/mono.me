using System.Text.RegularExpressions;

namespace _42.Monorepo.Cli.ConventionalCommits
{
    public class Parser
    {
        private const string TEMPLATE =
            @"^(?<type>[\w]+)(\((?<scope>[\w\-]+)\))?(?<breaking>!)?: ?((?<issue>([A-Z]+-|#)[\d]+) )?(?<description>.*)";

        private readonly Regex regex;

        public Parser()
        {
            regex = new Regex(TEMPLATE, RegexOptions.Compiled);
        }

        public bool TryParseCommitMessage(string message, out IConventionalCommitMessage? conventionalMessage)
        {
            var match = regex.Match(message);

            if (!match.Success)
            {
                conventionalMessage = null;
                return false;
            }

            conventionalMessage = new ConventionalCommitMessage(
                match.Groups["type"].Value,
                match.Groups["description"].Value,
                match.Groups.ContainsKey("breaking"),
                match.Groups.ContainsKey("scope") ? match.Groups["scope"].Value : string.Empty,
                match.Groups.ContainsKey("issue") ? match.Groups["issue"].Value : string.Empty);
            return true;
        }
    }
}
