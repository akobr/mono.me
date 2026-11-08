using _42.Testing.UI.Automation.Runtime.Settings;

namespace _42.Platform.Automation.Shared;

// The keys of the Target section that every Platform automation project has, next to the library's BaseUrl and SignIn:
// the pre-provisioned organization, project and view the tests work in, and the projects of the exact roles. What
// BaseUrl is belongs to the project: the admin UI's URL for the admin UI's tests, the API's base (with /api) for the
// CLI's. A project derives its own Target type with its own keys (the admin UI's PlatformTarget, the CLI's CliTarget),
// reads it with Settings.Bind<T>(PlatformTargetSettings.Section) and checks it in IUiAutomationSetup.Validate with
// Validate, before its own keys.
public class PlatformTargetSettings : TargetSettings
{
    public const string Section = "Target";

    private Dictionary<string, string> roleProjects = new(StringComparer.OrdinalIgnoreCase);

    public string Organization { get; set; } = string.Empty;

    public string Project { get; set; } = string.Empty;

    public string View { get; set; } = string.Empty;

    // By a Role name (Contributor, Reader, …; case-insensitive), "<org>/<project>[/<view>]": a project in which every
    // pool user holds that role, so a test of the role ([UserRole]) works there with any pool user. A role without an
    // entry leases a pool user who holds it in Target:Project. Empty by default; the admin UI's mock maps Contributor and
    // Reader to the projects where Ada Lovelace holds them. The target check verifies each entry.
    public Dictionary<string, string> RoleProjects
    {
        get => roleProjects;
        set => roleProjects = new Dictionary<string, string>(value ?? [], StringComparer.OrdinalIgnoreCase);
    }

    // "<org>/<project>[/<view>]" that RoleProjects maps role to; null for Role.Any and a role without an entry.
    public string? RoleProjectOf(Role role)
    {
        return role != Role.Any && RoleProjects.TryGetValue(role.ToString(), out var project) ? project : null;
    }

    // Adds a message to errors for each shared key that is wrong: Organization, Project and View are Platform names, and
    // each RoleProjects entry is a role a test acts as (not Any) with "<org>/<project>[/<view>]" of Platform names.
    public void Validate(ICollection<string> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        RequireName(Organization, $"{Section}:Organization", errors);
        RequireName(Project, $"{Section}:Project", errors);
        RequireName(View, $"{Section}:View", errors);

        foreach (var (role, project) in RoleProjects)
        {
            CheckRoleProject(role, project, errors);
        }
    }

    // A Target:RoleProjects entry: a role of [UserRole] (not Any) and "<org>/<project>[/<view>]" of Platform names.
    private static void CheckRoleProject(string role, string project, ICollection<string> errors)
    {
        if (!role.All(char.IsLetter) || !Enum.TryParse<Role>(role, ignoreCase: true, out var parsed) || parsed == Role.Any)
        {
            var roles = string.Join(", ", Enum.GetNames<Role>().Where(name => name != nameof(Role.Any)));
            errors.Add($"{Section}:RoleProjects:{role} is not a role a test acts as; use one of {roles}.");
        }

        var parts = (project ?? string.Empty).Split('/');
        if (parts.Length is not (2 or 3) || !parts.All(PlatformNames.IsValid))
        {
            errors.Add($"{Section}:RoleProjects:{role} '{project}' is not <organization>/<project>[/<view>] of Platform names: 2 to 63 lower-case letters, digits and hyphens each.");
        }
    }

    private static void RequireName(string value, string setting, ICollection<string> errors)
    {
        if (!PlatformNames.IsValid(value))
        {
            errors.Add($"{setting} '{value}' is not a Platform name: 2 to 63 lower-case letters, digits and hyphens, not starting with a hyphen.");
        }
    }
}
