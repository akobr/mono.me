using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Running.Processes;
using _42.Testing.UI.Automation.Runtime;
using _42.Testing.UI.Automation.Runtime.Users;

namespace _42.Platform.Cli.AutomationTests;

// Whom a test acts as and where, as in the admin UI's suite: [UserRole(Role.X)] on the test method (or its class; the
// method's wins) decides the pool user it leases (RequiredUser) and, through Target:RoleProjects, the project it works in
// (ProjectKey, View, InProject). A role that Target:RoleProjects maps runs there with any pool user; otherwise the test
// leases a pool user who holds the role in Target:Project. [MachineOnly] leases nobody. UseProject (and
// CreateDisposableProjectAsync) moves the acting project to another one for the rest of the test. The mode attributes
// skip a test before its lease (GetSkipReason): [Terminal] and [DisposableProject] where no pseudo terminal is supported,
// [RequiresFeature] unless Target:Features lists the feature, [RequiresMachine] unless Machines:<name> is configured.
//
// Feature users: the target marks the pool user a feature stands for (CliFeatures.OfUsers: Registration,
// MultiOrganization) with the feature's name among its Roles. A [RequiresFeature] test of such a feature leases that user
// (FeatureUser), whatever its [UserRole]; every other lease of the suite (RequiredUser, AsAnotherUserAsync, ArrangeAsAsync,
// AsRoleUserAsync, AsGrantedUserAsync) excludes those users (WithoutFeatureUsers), so an unregistered user or one of two
// organizations never acts in an ordinary test.
public abstract partial class CliTest
{
    private readonly HashSet<string> ownProjects = new(StringComparer.Ordinal);
    private Role? actingRole;
    private (string Key, string View, bool Own)? actingProject;

    // Administrator or Owner.
    protected static UserRequirement AdminUser => PlatformRoles.Admin;

    // Contributor and up.
    protected static UserRequirement EditingUser => PlatformRoles.Editing;

    // ContributorWithSecrets, Administrator or Owner.
    protected static UserRequirement SecretsUser => PlatformRoles.Secrets;

    // The role the running test acts as: its [UserRole], Role.Any without one.
    protected Role ActingRole
    {
        get
        {
            if (actingRole is { } known)
            {
                return known;
            }

            var method = TestContext.Current?.Metadata.TestDetails.MethodName;
            return method is null ? UserRoleAttribute.Of(GetType(), null) : (actingRole = UserRoleAttribute.Of(GetType(), method)).Value;
        }
    }

    // "<org>/<project>[/<view>]" that Target:RoleProjects maps the acting role to; null for Role.Any and a role without
    // an entry.
    protected string? RoleProject => Target.RoleProjectOf(ActingRole);

    // The organization of the project the test acts in: the project of UseProject (a disposable one), else the role's
    // project of Target:RoleProjects, else Target:Organization.
    protected string Organization => actingProject is { } acting ? acting.Key[..acting.Key.IndexOf('.', StringComparison.Ordinal)] : RoleProjectPart(0) ?? Target.Organization;

    protected string Project => actingProject is { } acting ? acting.Key[(acting.Key.IndexOf('.', StringComparison.Ordinal) + 1)..] : RoleProjectPart(1) ?? Target.Project;

    // The view: the one of UseProject, else the role project's (default when it names none), else Target:View.
    protected string View => actingProject?.View ?? (RoleProject is null ? Target.View : RoleProjectPart(2) ?? "default");

    // "<organization>.<project>", what -p takes.
    protected string ProjectKey => $"{Organization}.{Project}";

    // Whether the test acts in a project of its own (CreateDisposableProjectAsync, UseProject with one it created).
    protected bool ActsInOwnProject => actingProject is { Own: true };

    // The pool user of the test: the feature user of a [RequiresFeature] of a user feature (UserFeature); otherwise the
    // user of the acting role, never a feature user: any user in a project of Target:RoleProjects (every pool user holds
    // the role there), else PlatformRoles.RequirementOf the role.
    protected override UserRequirement RequiredUser => UserFeature is { } feature
        ? FeatureUser(feature)
        : WithoutFeatureUsers(RoleProject is not null ? UserRequirement.Any : PlatformRoles.RequirementOf(ActingRole));

    // The user feature (CliFeatures.OfUsers) a [RequiresFeature] of the running test names; null for any other test.
    protected string? UserFeature => Categories
        .Where(category => category.StartsWith(CliCategories.RequiresPrefix, StringComparison.Ordinal))
        .Select(category => category[CliCategories.RequiresPrefix.Length..])
        .FirstOrDefault(feature => CliFeatures.OfUsers.Contains(feature, StringComparer.OrdinalIgnoreCase));

    // [MachineOnly] tests lease no pool user.
    protected override bool LeasesUser => !HasCategory(CliCategories.MachineOnly);

    // The categories of the running test (TUnit's metadata): the mode attributes, [Elementary], [Area].
    protected IReadOnlyCollection<string> Categories => (IReadOnlyCollection<string>?)TestContext.Current?.Metadata.TestDetails.Categories ?? [];

    protected bool HasCategory(string category)
    {
        return Categories.Contains(category, StringComparer.Ordinal);
    }

    // The pool user who stands for the feature (CliFeatures.Registration, MultiOrganization): the one whose Roles in
    // Users:Pool list the feature's name. A target that lists the feature without such a user fails the lease with the
    // requirement.
    protected static UserRequirement FeatureUser(string feature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(feature);
        return UserRequirement.WithRole(feature);
    }

    // The requirement, and the user stands for no feature (CliFeatures.OfUsers) but one the requirement asks for itself
    // (FeatureUser): what every lease of the suite goes through.
    protected static UserRequirement WithoutFeatureUsers(UserRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        return requirement.Except([.. CliFeatures.OfUsers.Where(feature => !requirement.Roles.Contains(feature, StringComparer.OrdinalIgnoreCase))]);
    }

    // The project Target:RoleProjects maps the role to, as -p and -v take it: "<organization>.<project>" and its view
    // ("default" when the entry names none); null for Role.Any and a role without an entry.
    protected (string ProjectKey, string View)? RoleProjectOf(Role role)
    {
        if (Target.RoleProjectOf(role) is not { } mapped)
        {
            return null;
        }

        var parts = mapped.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return ($"{parts[0]}.{parts[1]}", parts.Length > 2 ? parts[2] : "default");
    }

    // Another pool user who holds exactly the role, and the project and view where: "<contributor project>" or "<reader
    // project>" of the catalogue for a step of another user. Any other pool user in the project Target:RoleProjects maps
    // the role to (every pool user holds it there), else a pool user who holds the role in Target:Project
    // (PlatformRoles.RequirementOf) with Target:Project and Target:View. Its runs take the project's -p and -v.
    protected async Task<(SformActor Actor, string ProjectKey, string View)> AsRoleUserAsync(Role role)
    {
        if (RoleProjectOf(role) is { } mapped)
        {
            return (await AsAnotherUserAsync(UserRequirement.Any), mapped.ProjectKey, mapped.View);
        }

        return (await AsAnotherUserAsync(PlatformRoles.RequirementOf(role)), Target.ProjectKey, Target.View);
    }

    // From now on the test acts in the project: ProjectKey, Organization, Project, View and InProject(…) name it, and
    // CreateMachineAsync creates there (e.g. a project of CreateDisposableProjectAsync(actInIt: false), or back to
    // Target:Project with Target.ProjectKey). "default" when no view is given.
    protected void UseProject(string projectKey, string view = "default")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(view);
        var dot = projectKey.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0 || dot == projectKey.Length - 1)
        {
            throw new ArgumentException($"'{projectKey}' is no project key (<organization>.<project>).", nameof(projectKey));
        }

        actingProject = (projectKey, view, ownProjects.Contains(projectKey));
    }

    protected override string? GetSkipReason(TestContext context, AutomationSession session)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(session);

        actingRole ??= UserRoleAttribute.Of(GetType(), context.Metadata.TestDetails.MethodName);
        if (base.GetSkipReason(context, session) is { } reason)
        {
            return reason;
        }

        var categories = context.Metadata.TestDetails.Categories;
        if ((categories.Contains(CliCategories.Terminal) || categories.Contains(CliCategories.DisposableProject))
            && TerminalSupport.Reason(session.FileSystem) is { } terminal)
        {
            return $"Terminal: {terminal}";
        }

        var target = session.Settings.Bind<CliTarget>(PlatformTargetSettings.Section);
        foreach (var category in categories.Where(category => category.StartsWith(CliCategories.RequiresPrefix, StringComparison.Ordinal)))
        {
            var feature = category[CliCategories.RequiresPrefix.Length..];
            if (!target.HasFeature(feature))
            {
                return $"Requires the feature {feature}: Target:Features does not list it (docs/Platform/Cli.AutomationTests/automation-tests.md, \"Features\").";
            }
        }

        var machines = session.Settings.Bind<MachineCatalog>(MachineCatalog.Section);
        foreach (var category in categories.Where(category => category.StartsWith(RequiresMachineAttribute.MachinePrefix, StringComparison.Ordinal)))
        {
            var name = category[RequiresMachineAttribute.MachinePrefix.Length..];
            if (!machines.TryGetValue(name, out var machine) || !machine.HasCredential)
            {
                return $"Requires the machine {name}: Machines:{name} is not configured with a credential (Machines:{name}:ApiKey in the user secrets or UITESTS_Machines__{name}__ApiKey; docs/Platform/Cli.AutomationTests/automation-tests.md, \"Machines\").";
            }
        }

        return null;
    }

    private string? RoleProjectPart(int index)
    {
        var parts = RoleProject?.Split('/');
        return parts is not null && index < parts.Length ? parts[index] : null;
    }
}
