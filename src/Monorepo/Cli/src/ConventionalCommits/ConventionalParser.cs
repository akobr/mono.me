using System.Text.RegularExpressions;

namespace _42.Monorepo.Cli.ConventionalCommits
{
    public class ConventionalParser
    {
        private const string TEMPLATE = @"^(?<type>\:?[\w]+\:?)\s*(\((?<scope>[\w\-]+)\))?(?<breaking>!)?\s*:\s*((?<issue>([A-Z]+-|#)[\da-z]+)\s*)?(?<description>.*)";
        private const string ISSUE_ID_TEMPLATE = @"(?<issue>([A-Z]+-|#)[\da-z]+)";
        private const string URL_TEMPLATE = @"(http|https):\/\/([\w_-]+(?:(?:\.[\w_-]+)+))([\w.,@?^=%&:\/~+#-]*[\w@?^=%&\/~+#-])";

        private readonly Regex regex;
        private readonly Regex issueRegex;
        private readonly Regex urlRegex;
        private readonly string issueUrlTemplate;
        private readonly bool hasIssueUrlTemplate;

        public ConventionalParser(string? issueUrlTemplate)
        {
            regex = new Regex(TEMPLATE, RegexOptions.Compiled);
            issueRegex = new Regex(ISSUE_ID_TEMPLATE, RegexOptions.Compiled);
            urlRegex = new Regex(URL_TEMPLATE, RegexOptions.Compiled);

            hasIssueUrlTemplate = !string.IsNullOrWhiteSpace(issueUrlTemplate);
            this.issueUrlTemplate = issueUrlTemplate ?? "{0}";
        }

        public bool TryParseCommitMessage(string shortMessage, string fullMessage, out IConventionalMessage conventionalMessage)
        {
            var match = regex.Match(shortMessage);

            if (!match.Success)
            {
                conventionalMessage = new ConventionalMessage(string.Empty, string.Empty, false);
                return false;
            }

            var message = new ConventionalMessage(
                match.Groups["type"].Value,
                match.Groups["description"].Value,
                match.Groups["breaking"].Success)
            {
                Scope = match.Groups.ContainsKey("scope") ? match.Groups["scope"].Value : null,
                IssueLink = match.Groups.ContainsKey("issue") ? match.Groups["issue"].Value : null,
            };

            if (fullMessage.Contains("BREAKING CHANGE"))
            {
                message.IsBreakingChange = true;
            }

            if (hasIssueUrlTemplate)
            {
                if (!string.IsNullOrEmpty(message.IssueLink))
                {
                    message.Links.Add(BuildIssueUrl(message.IssueLink));
                }

                foreach (Match itemMatch in issueRegex.Matches(fullMessage))
                {
                    message.Links.Add(BuildIssueUrl(itemMatch.Value));
                }
            }

            foreach (Match urlMatch in urlRegex.Matches(fullMessage))
            {
                var url = urlMatch.Value;
                message.Links.Add(url);
            }

            conventionalMessage = message;
            return true;
        }

        private string BuildIssueUrl(string issueId)
        {
            if (issueId.StartsWith('#'))
            {
                issueId = issueId[1..];
            }

            return string.Format(issueUrlTemplate, issueId);
        }
    }
}
