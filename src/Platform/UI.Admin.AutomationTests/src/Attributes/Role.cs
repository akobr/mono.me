// In the root namespace like UserRoleAttribute, so a test writes [UserRole(Role.Reader)] without a using.
namespace _42.Platform.UI.Admin.AutomationTests;

// Whom a test acts as, by the project roles of the admin UI (app/composables/useRole.ts): a Reader reads; a Contributor
// creates, edits and manages machines; a ContributorWithSecrets also handles secrets; an Administrator also manages
// access, transfers and monitoring; an Owner also owns the project and the organization's operations.
// PlatformTest.RequiredUser turns it into the pool user the test leases, and Target:RoleProjects into the project it
// acts in.
public enum Role
{
    // Any pool user.
    Any,

    // Contributor, ContributorWithSecrets, Administrator or Owner: may create and edit.
    Editor,

    // Administrator or Owner.
    Admin,

    // ContributorWithSecrets, Administrator or Owner: may handle secrets.
    Secrets,

    // Owner, also of the target's organization (Users:Pool lists Owner for an organization Owner).
    Owner,

    // Exactly Contributor.
    Contributor,

    // Exactly Reader.
    Reader,
}
