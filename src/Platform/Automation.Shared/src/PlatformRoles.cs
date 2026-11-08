using _42.Testing.UI.Automation.Runtime.Users;

namespace _42.Platform.Automation.Shared;

// The project roles of the Platform as Users:Pool lists them (the Roles of a pool user: the user's roles in the target's
// organization and project), and the pool user each Role a test acts as leases.
public static class PlatformRoles
{
    public const string Owner = "Owner";
    public const string Administrator = "Administrator";
    public const string ContributorWithSecrets = "ContributorWithSecrets";
    public const string Contributor = "Contributor";
    public const string Reader = "Reader";

    // Contributor and up: may create and edit in the target's project; a Reader gets no create, edit or save action.
    public static UserRequirement Editing { get; } = UserRequirement.WithAnyRole(Owner, Administrator, ContributorWithSecrets, Contributor);

    // Administrator or Owner.
    public static UserRequirement Admin { get; } = UserRequirement.WithAnyRole(Owner, Administrator);

    // ContributorWithSecrets, Administrator or Owner: may handle secrets.
    public static UserRequirement Secrets { get; } = UserRequirement.WithAnyRole(Owner, Administrator, ContributorWithSecrets);

    // The pool user of a role in the target's project: Editor is Editing, Admin Admin, Secrets Secrets, and Owner,
    // Contributor and Reader exactly that role; Any any pool user. A role that Target:RoleProjects maps works in that
    // project with any pool user instead (the test base class decides).
    public static UserRequirement RequirementOf(Role role)
    {
        return role switch
        {
            Role.Editor => Editing,
            Role.Admin => Admin,
            Role.Secrets => Secrets,
            Role.Owner => UserRequirement.WithRole(Owner),
            Role.Contributor => UserRequirement.WithRole(Contributor),
            Role.Reader => UserRequirement.WithRole(Reader),
            _ => UserRequirement.Any,
        };
    }
}
