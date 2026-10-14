using System.Globalization;
using System.Text;

namespace _42.Platform.Storyteller.Transferring.Git;

// Author or committer of a commit.
public sealed record class GitSignature(string Name, string Email, DateTimeOffset When)
{
    public const string EmailDomain = "2s.platform";

    public static GitSignature System(DateTimeOffset when) => new("2S Platform", $"system@{EmailDomain}", when);

    // The author string of a configuration version becomes the name; the e-mail is derived from it:
    // "account: user_01" → "account: user_01 <account.user_01@2s.platform>".
    public static GitSignature FromAuthor(string? author, DateTimeOffset when)
    {
        var name = Sanitize(author);

        if (name.Length == 0)
        {
            name = "unknown";
        }

        var local = new StringBuilder(name.Length);

        foreach (var character in name.Replace(": ", ".", StringComparison.Ordinal))
        {
            local.Append(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-' ? character : '-');
        }

        var email = local.ToString().Trim('.', '-');
        return new GitSignature(name, $"{(email.Length == 0 ? "unknown" : email)}@{EmailDomain}", when);
    }

    // "Name <email> 1760405412 +0000"
    public string Format()
    {
        return $"{Sanitize(Name)} <{Sanitize(Email)}> {When.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)} +0000";
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            if (character is not ('<' or '>' or '\n' or '\r' or '\0'))
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Trim();
    }
}
