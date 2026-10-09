using System.Text.RegularExpressions;

namespace _42.Platform.Storyteller;

// Names of organizations, projects and views become route segments and parts of keys and container names,
// so they are lower-case, without dots ('.' separates key segments), and never a route word of the API or the admin UI.
// Only new names are checked; documents created before these rules stay readable.
public static partial class NameRules
{
    public const string Pattern = "^[a-z0-9][a-z0-9-]{1,62}$";

    public static readonly IReadOnlySet<string> ReservedOrganizationNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "access", "auth", "login", "callback", "onboarding", "invitations", "account", "orgs", "unsupported",
    };

    public static readonly IReadOnlySet<string> ReservedProjectAndViewNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "access", "views", "members", "invitations", "machines", "certificates", "settings",
    };

    public static bool IsValidName(string? name)
    {
        return name is not null && NamePattern().IsMatch(name);
    }

    public static void EnsureOrganizationName(string? name) => Ensure(name, "organization", ReservedOrganizationNames);

    public static void EnsureProjectName(string? name) => Ensure(name, "project", ReservedProjectAndViewNames);

    public static void EnsureViewName(string? name) => Ensure(name, "view", ReservedProjectAndViewNames);

    private static void Ensure(string? name, string kind, IReadOnlySet<string> reserved)
    {
        if (!IsValidName(name))
        {
            throw new InvalidInputException(
                $"The {kind} name '{name}' is not valid: use 2 to 63 lower-case letters, digits or hyphens, starting with a letter or a digit.",
                ErrorCodes.InvalidName);
        }

        if (reserved.Contains(name!))
        {
            throw new InvalidInputException($"The {kind} name '{name}' is reserved.", ErrorCodes.InvalidName);
        }
    }

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
