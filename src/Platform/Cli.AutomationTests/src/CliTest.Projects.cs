using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Platform.Cli.AutomationTests.Running.Terminal;
using _42.Testing.UI.Automation.Runtime.Users;

namespace _42.Platform.Cli.AutomationTests;

// A project of the test's own for a case that changes project-wide state (mode P, [DisposableProject]; the catalogue's P
// cases are Owner cases: the leased Owner creates the project and acts as its Owner). sform creates a project only
// through sform account points create, which asks for the organization and the name and has no options for them (it ends
// with 43 on redirected input), so it runs in a pseudo terminal: the organization is picked by typing its name into the
// selection (Sharprompt filters the list), the name typed. The project stays in the organization.
//
// A step "as the second user" of a P case runs as another pool user to whom the Owner granted a role in the project
// (AsGrantedUserAsync: lease, account id, account points grant). An organization role grants nothing in a project
// (access.md), so any pool user then holds exactly the granted role there, whatever its role in Target:Project.
public abstract partial class CliTest
{
    // Creates "<Target:Organization>.<generated name>" as the leased user, who must be an Owner of the organization
    // ([UserRole(Role.Owner)]), and returns its key ("<org>.<project>"); its default view is "default". With actInIt
    // (the default) the test acts in it from then on (UseProject: ProjectKey, View, InProject and CreateMachineAsync name
    // it); Target.ProjectKey stays the target project (the <A> of a context case). points create sets the new project as
    // the home's default, which is removed again, as every test starts without one.
    protected async Task<string> CreateDisposableProjectAsync(bool actInIt = true)
    {
        if (!User.HasRole(PlatformRoles.Owner))
        {
            throw new InvalidOperationException(
                $"A disposable project needs an Owner of Target:Organization '{Target.Organization}', and {User} is none; mark the test [UserRole(Role.Owner)] (every P case of the catalogue is an Owner case).");
        }

        var name = CliData.ProjectName();
        await using var terminal = await StartSformInTerminal(new SformRunOptions { Label = $"create the disposable project {name}" }, "account", "points", "create");
        await terminal.ExpectAsync("Select existing organization or create new");
        await terminal.SendAsync(Target.Organization);
        await terminal.ExpectAsync(Target.Organization);
        await terminal.SendKeyAsync(TerminalKey.Enter);
        await terminal.ExpectAsync("Name of your new project");
        await terminal.SendLineAsync(name);
        (await terminal.WaitForExitAsync()).ShouldSucceed();

        Home.ResetDefaults();
        var key = $"{Target.Organization}.{name}";
        ownProjects.Add(key);
        Scope.Properties["disposableProject"] = key;
        if (actInIt)
        {
            UseProject(key);
        }

        return key;
    }

    // The account id of the leased user ("Account ID: #<id>" of sform account), as account points grant takes it.
    protected async Task<string> AccountIdAsync()
    {
        return AccountOutput.AccountIdOf(await Sform(new SformRunOptions { Label = "the account id" }, "account"));
    }

    // Grants the role ("Reader", "Contributor", "ContributorWithSecrets", "Administrator", "Owner"; PlatformRoles) to the
    // account in a project of the test's own (the acting project by default), as the leased user:
    // sform account points grant <id> -p <project> -r <role>, which must succeed. Refused for any other project, where
    // the pool users' roles are fixed (a grant only raises a role, and nothing lowers it again).
    protected async Task GrantAsync(string accountId, string role, string? projectKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        var project = projectKey ?? ProjectKey;
        if (!ownProjects.Contains(project))
        {
            throw new InvalidOperationException(
                $"The test grants roles only in a project of its own (CreateDisposableProjectAsync), not in {project}: the roles of the pool users in the target's projects are fixed.");
        }

        (await Sform(new SformRunOptions { Label = $"grant {role} to {accountId}" }, "account", "points", "grant", accountId, "-p", project, "-r", role))
            .ShouldSucceed();
    }

    // Another pool user of the test (any by default, or one of the requirement) to whom the leased user granted the role
    // in a project of the test's own (the acting project by default): the "second user" of a P case, e.g. a
    // ContributorWithSecrets who is refused -m (MCH-LIF-10) or an Administrator who cannot grant Owner (ACC-PNT-07). Its
    // AccountId is known; its runs (SformActor.Sform, CreateMachineAsync with MachineCreation.As) use its own home.
    protected async Task<SformActor> AsGrantedUserAsync(string role, UserRequirement? requirement = null, string? projectKey = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        var other = await AsAnotherUserAsync(requirement ?? UserRequirement.Any);
        var accountId = await other.AccountIdAsync();
        await GrantAsync(accountId, role, projectKey);
        return other;
    }
}
