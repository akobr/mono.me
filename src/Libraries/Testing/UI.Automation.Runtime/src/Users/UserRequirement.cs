namespace _42.Testing.UI.Automation.Runtime.Users;

// Which pool user a test needs: any user, or one that has at least one of the roles.
public sealed class UserRequirement
{
    private UserRequirement(IReadOnlyList<string> roles)
    {
        Roles = roles;
    }

    public static UserRequirement Any { get; } = new([]);

    public IReadOnlyList<string> Roles { get; }

    public static UserRequirement WithRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        return new UserRequirement([role]);
    }

    public static UserRequirement WithAnyRole(params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        return roles.Length == 0 ? Any : new UserRequirement(roles.Where(role => !string.IsNullOrWhiteSpace(role)).ToArray());
    }

    public bool IsSatisfiedBy(TestUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Roles.Count == 0 || Roles.Any(user.HasRole);
    }

    public override string ToString()
    {
        return Roles.Count == 0 ? "any role" : $"role {string.Join(" or ", Roles)}";
    }
}
