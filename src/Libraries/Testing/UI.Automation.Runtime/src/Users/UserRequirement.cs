namespace _42.Testing.UI.Automation.Runtime.Users;

// Which pool user a test needs: any user, or one that has at least one of the roles; and, with Except, none of the
// excluded roles (e.g. the users who stand for a feature, which only the tests of that feature lease). Immutable: Except
// returns a new requirement. Roles compare without case, as TestUser.HasRole does.
public sealed class UserRequirement
{
    private UserRequirement(IReadOnlyList<string> roles, IReadOnlyList<string> excludedRoles)
    {
        Roles = roles;
        ExcludedRoles = excludedRoles;
    }

    public static UserRequirement Any { get; } = new([], []);

    // The user has at least one of them; empty for any role.
    public IReadOnlyList<string> Roles { get; }

    // The user has none of them (Except); empty by default.
    public IReadOnlyList<string> ExcludedRoles { get; }

    public static UserRequirement WithRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        return new UserRequirement([role], []);
    }

    public static UserRequirement WithAnyRole(params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        return roles.Length == 0 ? Any : new UserRequirement(roles.Where(role => !string.IsNullOrWhiteSpace(role)).ToArray(), []);
    }

    // The same requirement, and the user has none of the roles besides: e.g. UserRequirement.Any.Except("Registration")
    // never leases the pool user who stands for an unregistered account. Blank roles and roles excluded already are
    // ignored; a requirement that excludes nothing new is returned as it is.
    public UserRequirement Except(params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        var added = roles
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim())
            .Where(role => !ExcludedRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return added.Length == 0 ? this : new UserRequirement(Roles, [.. ExcludedRoles, .. added]);
    }

    public bool IsSatisfiedBy(TestUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return (Roles.Count == 0 || Roles.Any(user.HasRole)) && !ExcludedRoles.Any(user.HasRole);
    }

    public override string ToString()
    {
        var roles = Roles.Count == 0 ? "any role" : $"role {string.Join(" or ", Roles)}";
        return ExcludedRoles.Count == 0 ? roles : $"{roles} and none of {string.Join(", ", ExcludedRoles)}";
    }
}
