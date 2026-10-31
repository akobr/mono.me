using _42.Testing.UI.Automation.Runtime.Data;
using _42.Testing.UI.Automation.Runtime.Settings;

namespace _42.Testing.UI.Automation.Runtime.Users;

// A user of Users:Pool. ToString and reports carry the Fingerprint, never the e-mail or the password.
public sealed class TestUser
{
    public TestUser(int index, string email, string? password = null, string? displayName = null, IReadOnlyList<string>? roles = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        Index = index;
        Email = email.Trim();
        Password = password;
        DisplayName = displayName;
        Roles = roles ?? [];
        Fingerprint = $"u-{StableHash.Fnv1a32(Email.ToLowerInvariant()):x8}";
    }

    // The position in Users:Pool.
    public int Index { get; }

    public string Email { get; }

    public string? Password { get; }

    public string? DisplayName { get; }

    public IReadOnlyList<string> Roles { get; }

    // "u-" and 8 hex digits of a stable hash of the lower-case e-mail; the same in every run.
    public string Fingerprint { get; }

    public static IReadOnlyList<TestUser> FromSettings(UsersSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return settings.Pool
            .Select((user, index) => new TestUser(index, user.Email ?? string.Empty, user.Password, user.DisplayName, user.Roles.ToArray()))
            .ToArray();
    }

    public bool HasRole(string role)
    {
        return Roles.Contains(role, StringComparer.OrdinalIgnoreCase);
    }

    public override string ToString()
    {
        return $"Users:Pool:{Index} ({Fingerprint})";
    }
}
