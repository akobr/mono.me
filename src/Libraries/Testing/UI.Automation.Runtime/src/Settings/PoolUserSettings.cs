namespace _42.Testing.UI.Automation.Runtime.Settings;

// One entry of Users:Pool.
public sealed class PoolUserSettings
{
    public string? Email { get; set; }

    // Required only when the sign-in flow needs one.
    public string? Password { get; set; }

    // The name the application shows for the user, when a test needs it (e.g. "User menu for <name>").
    public string? DisplayName { get; set; }

    public List<string> Roles { get; set; } = [];
}
