using System.Collections.Concurrent;
using System.Reflection;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation.Runtime;
using _42.Testing.UI.Automation.Runtime.Users;

namespace _42.Platform.UI.Admin.AutomationTests;

// Whom a test acts as and where: [UserRole(Role.X)] on the test method (or its class; the method's wins) decides the
// pool user it leases (RequiredUser) and, through Target:RoleProjects, the project it works in (Routes). A role that
// Target:RoleProjects maps (on the mock Contributor to acme/shipping and Reader to northwind/portal, where Ada Lovelace
// holds exactly that role) runs there with any pool user; otherwise the test leases a pool user who holds the role in
// the target's project. A derived class may still override RequiredUser. The mode attributes [DesktopOnly] and
// [PhoneOnly], and a role the mock cannot act as, skip the test before its lease (GetSkipReason).
public abstract partial class PlatformTest
{
    // The project roles of the Platform, as Users:Pool lists them.
    private const string OwnerRole = "Owner";
    private const string AdministratorRole = "Administrator";
    private const string ContributorWithSecretsRole = "ContributorWithSecrets";
    private const string ContributorRole = "Contributor";
    private const string ReaderRole = "Reader";

    // [UserRole] of a test method, else of its class, by class and method; reflection, because TUnit's metadata keeps
    // only attributes related to TUnit types.
    private static readonly ConcurrentDictionary<(Type Type, string Method), Role> TestRoles = new();

    private Role? actingRole;

    // Administrator or Owner.
    protected static UserRequirement AdminUser { get; } = UserRequirement.WithAnyRole(OwnerRole, AdministratorRole);

    // ContributorWithSecrets, Administrator or Owner: may handle secrets.
    protected static UserRequirement SecretsUser { get; } = UserRequirement.WithAnyRole(OwnerRole, AdministratorRole, ContributorWithSecretsRole);

    // The role the running test acts as: its [UserRole], Role.Any without one.
    protected Role ActingRole
    {
        get
        {
            if (actingRole is { } known)
            {
                return known;
            }

            // Outside a test (no context) only the class's attribute is known; not kept.
            var method = TestContext.Current?.Metadata.TestDetails.MethodName;
            return method is null ? RoleOf(GetType(), null) : (actingRole = RoleOf(GetType(), method)).Value;
        }
    }

    // "<org>/<project>[/<view>]" that Target:RoleProjects maps the acting role to; null for Role.Any and a role without
    // an entry.
    protected string? RoleProject => ActingRole != Role.Any && Target.RoleProjects.TryGetValue(ActingRole.ToString(), out var project) ? project : null;

    // The routes of the project the test acts in: the role's project of Target:RoleProjects (its view, or default), or
    // the target's organization, project and view.
    protected AppRoutes Routes => RoleProject is { } project ? AppRoutes.Parse(project) : AppRoutes.For(Target);

    // The pool user of the acting role: any user in a project of Target:RoleProjects (every pool user holds the role
    // there); otherwise Editor is EditingUser, Admin AdminUser, Secrets SecretsUser, and Owner, Contributor and Reader
    // that role.
    protected override UserRequirement RequiredUser => RoleProject is not null
        ? UserRequirement.Any
        : ActingRole switch
        {
            Role.Editor => EditingUser,
            Role.Admin => AdminUser,
            Role.Secrets => SecretsUser,
            Role.Owner => UserRequirement.WithRole(OwnerRole),
            Role.Contributor => UserRequirement.WithRole(ContributorRole),
            Role.Reader => UserRequirement.WithRole(ReaderRole),
            _ => UserRequirement.Any,
        };

    // Before the lease: [DesktopOnly] below App.DockedSidebarWidth, [PhoneOnly] at App.PhoneWidth and wider (the session's
    // viewport, the device's or Browser:Viewport), and on the mock a role that no pool user holds and Target:RoleProjects
    // does not map. On a deployed target such a test stays Blocked by its lease: there it is a gap of the environment.
    protected override string? GetSkipReason(TestContext context, AutomationSession session)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(session);

        actingRole ??= RoleOf(GetType(), context.Metadata.TestDetails.MethodName);
        if (base.GetSkipReason(context, session) is { } reason)
        {
            return reason;
        }

        var categories = context.Metadata.TestDetails.Categories;
        var viewport = session.ContextOptions.Viewport;
        var device = session.ContextOptions.Device is { } name ? $" ({name})" : string.Empty;
        if (categories.Contains(PlatformCategories.DesktopOnly) && viewport is { Width: < App.DockedSidebarWidth })
        {
            return $"Desktop only: the viewport is {viewport.Width} px wide{device}, below the desktop layout's {App.DockedSidebarWidth} px.";
        }

        if (categories.Contains(PlatformCategories.PhoneOnly) && viewport is not { Width: < App.PhoneWidth })
        {
            return viewport is null
                ? "Phone only: the session emulates no viewport."
                : $"Phone only: the viewport is {viewport.Width} px wide{device}, not below the phone layout's {App.PhoneWidth} px.";
        }

        if (Target.IsMock && !session.Users.Users.Any(RequiredUser.IsSatisfiedBy))
        {
            var unmapped = ActingRole == Role.Any ? string.Empty : $", and Target:RoleProjects maps no project to {ActingRole}";
            return $"The mock has no user with {RequiredUser}: every user of its Users:Pool stands for Ada Lovelace{unmapped}.";
        }

        return null;
    }

    private static Role RoleOf(Type type, string? method)
    {
        return method is null
            ? type.GetCustomAttribute<UserRoleAttribute>(inherit: true)?.Role ?? Role.Any
            : TestRoles.GetOrAdd((type, method), static key =>
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                return key.Type.GetMethods(flags)
                    .Where(candidate => string.Equals(candidate.Name, key.Method, StringComparison.Ordinal))
                    .Select(candidate => candidate.GetCustomAttribute<UserRoleAttribute>(inherit: true))
                    .FirstOrDefault(attribute => attribute is not null)?.Role
                    ?? key.Type.GetCustomAttribute<UserRoleAttribute>(inherit: true)?.Role
                    ?? Role.Any;
            });
    }
}
