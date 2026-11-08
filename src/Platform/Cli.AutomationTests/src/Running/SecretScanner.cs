using System.Text.RegularExpressions;

namespace _42.Platform.Cli.AutomationTests.Running;

// The credentials sform prints once, found in the output of a run before it is attached, so the runner registers them
// as secrets of the test (TestScope.AddSecret) and the attachment, the report and every later message show them as ***:
//   - the JSON members of a created or reset machine: "AccessKey" (the API key or the client secret), "ClientSecret",
//     "CertificatePassword", "AccessToken", "RefreshToken" (not the placeholders "[see below]" and "[written to file]");
//   - the lines "Client secret:     <value>" and "Certificate password: <value>";
//   - anything shaped like a structured API key, 2s.<machine segment>.<secret>;
//   - the values of the GitHub Actions mask commands, "::add-mask::<value>" (config export --github-env/--mask, which
//     sform prints for exactly the values it treats as secret), with the workflow command escaping (%25, %0D, %0A) undone.
// The runner scans the plain standard output, where the folds of WriteJson's 80 columns are joined already
// (SformResult.PlainStdOut), so a key the renderer split over two lines is found whole.
public static partial class SecretScanner
{
    public static IReadOnlyList<string> Find(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return [];
        }

        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in JsonMemberPattern().Matches(plainText))
        {
            var value = match.Groups["value"].Value;
            if (!value.StartsWith('['))
            {
                found.Add(value);
            }
        }

        foreach (Match match in LinePattern().Matches(plainText))
        {
            found.Add(match.Groups["value"].Value.Trim());
        }

        foreach (Match match in ApiKeyPattern().Matches(plainText))
        {
            found.Add(match.Value);
        }

        foreach (Match match in MaskPattern().Matches(plainText))
        {
            var value = UnescapeWorkflowCommand(match.Groups["value"].Value.TrimEnd('\r'));
            found.Add(value);
            foreach (var line in value.Split('\n'))
            {
                found.Add(line.TrimEnd('\r'));
            }
        }

        return found.Where(value => value.Trim().Length >= 4).ToArray();
    }

    // The data of a workflow command as GitHub Actions reads it: %25 is %, %0D a CR, %0A a LF.
    public static string UnescapeWorkflowCommand(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return EscapePattern().Replace(value, match => match.Value.ToUpperInvariant() switch
        {
            "%25" => "%",
            "%0D" => "\r",
            _ => "\n",
        });
    }

    [GeneratedRegex(@"""(?:AccessKey|ClientSecret|CertificatePassword|AccessToken|RefreshToken)""\s*:\s*""(?<value>[^""\s]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex JsonMemberPattern();

    [GeneratedRegex(@"(?:Client secret|Certificate password):\s*(?<value>\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex LinePattern();

    [GeneratedRegex(@"\b2s\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}", RegexOptions.CultureInvariant)]
    private static partial Regex ApiKeyPattern();

    [GeneratedRegex(@"^\s*::add-mask::(?<value>.+)$", RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex MaskPattern();

    [GeneratedRegex("%(?:25|0D|0A)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EscapePattern();
}
