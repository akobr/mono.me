namespace _42.Platform.Automation.Shared;

// Whom a test acts as, by the project roles of the Platform (the admin UI's app/composables/useRole.ts): a Reader reads;
// a Contributor creates, edits and manages machines; a ContributorWithSecrets also handles secrets; an Administrator
// also manages access, transfers and monitoring; an Owner also owns the project and the organization's operations.
// PlatformRoles.RequirementOf turns it into the pool user a test leases, and Target:RoleProjects
// (PlatformTargetSettings.RoleProjects) into the project it acts in. A project's own namespace usually imports this one
// globally, so a test writes [UserRole(Role.Reader)] without a using.
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
