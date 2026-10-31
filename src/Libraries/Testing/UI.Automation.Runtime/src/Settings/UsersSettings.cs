namespace _42.Testing.UI.Automation.Runtime.Settings;

// The Users section: the pre-provisioned users a test leases for its duration.
public sealed class UsersSettings
{
    // How long a lease waits for a free user before the test is blocked.
    public int LeaseTimeoutSeconds { get; set; } = 120;

    // Never lists a password in a committed file: Users:Pool:<n>:Password goes to user secrets or
    // UITESTS_Users__Pool__<n>__Password.
    public List<PoolUserSettings> Pool { get; set; } = [];
}
