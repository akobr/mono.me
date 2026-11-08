using System.Text.RegularExpressions;

namespace _42.Platform.Automation.Shared;

// The names of the Platform's organizations, projects and views (NameRules.Pattern of Backend.Core): 2 to 63 lower-case
// letters, digits and hyphens, not starting with a hyphen.
public static partial class PlatformNames
{
    public static bool IsValid(string? name)
    {
        return name is not null && Pattern().IsMatch(name);
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9-]{1,62}\z", RegexOptions.CultureInvariant)]
    public static partial Regex Pattern();
}
