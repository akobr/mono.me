using System.Text.Json.Nodes;
using _42.Platform.Cli.AutomationTests.Access;
using _42.Platform.Cli.AutomationTests.Running;
using _42.Platform.Cli.AutomationTests.Running.Terminal;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using _42.Testing.UI.Automation.Runtime.Users;

namespace _42.Platform.Cli.AutomationTests.Tests.Account;

// Access Points: sform account points lists the user's projects and organizations, gets the members of one, creates a
// project, and grants and revokes the roles of other users (the grants only in a disposable project of the test's own).
[Area(Area.Account)]
public sealed class AccessPointTests : CliTest
{
    // The roles of account points grant and revoke -r, Reader to Owner, as sform prints them.
    private static readonly string[] Roles =
    [
        PlatformRoles.Reader,
        PlatformRoles.Contributor,
        PlatformRoles.ContributorWithSecrets,
        PlatformRoles.Administrator,
        PlatformRoles.Owner,
    ];

    // ACC-PNT-01: account points lists the user's access points with the roles.
    // sform account points prints "# Access points" and a line "- <key> [<role>]" per access point; the target project is
    // listed with a role, the user's role there as Users:Pool records it, and the run ends with 0.
    [Test]
    [TestCase(272)]
    [UserRole(Role.Any)]
    [Covers("account points")]
    public async Task Points_List_ShowsRoles()
    {
        var list = (await Sform("account", "points"))
            .ShouldSucceed()
            .ShouldHaveLine("# Access points");

        var role = AccessPointOutput.RoleOf(list, ProjectKey) ?? throw list.Fail($"listed no line \"- {ProjectKey} [<role>]\"");
        var poolRoles = User.Roles.Where(name => Roles.Contains(name, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (poolRoles.Length > 0 && !poolRoles.Contains(role, StringComparer.OrdinalIgnoreCase))
        {
            throw list.Fail($"listed {ProjectKey} with the role {role}, while Users:Pool gives {User} the role {string.Join(" or ", poolRoles)} there");
        }
    }

    // ACC-PNT-02: points get -p shows the members and roles of a project to an Administrator; an unknown key fails with
    // exit 2.
    // As an Administrator (or Owner) of the target project, sform account points get -p <org>.<project> and the same with
    // --pointKey print "# Access point <org>.<project>" and a line " - <account id> [<role>]" per member, the leased user's
    // own id among them as Administrator or Owner; sform account points get -p <org>.<generated> ends with 2 and "No access
    // point with key '<org>.<generated>' has been found."
    [Test]
    [TestCase(273)]
    [UserRole(Role.Admin)]
    [Covers("account points get", "-p", "--pointKey")]
    public async Task Points_Get_ShowsMembers()
    {
        var accountId = await AccountIdAsync();

        foreach (var option in new[] { "-p", "--pointKey" })
        {
            var get = (await Sform("account", "points", "get", option, ProjectKey))
                .ShouldSucceed()
                .ShouldHaveLine($"# Access point {ProjectKey}");

            var role = AccessPointOutput.RoleOf(get, accountId) ?? throw get.Fail($"listed no member line \" - {accountId} [<role>]\" of the leased user");
            await Assert.That(new[] { PlatformRoles.Administrator, PlatformRoles.Owner }).Contains(role);
        }

        var unknown = $"{Organization}.{CliData.ProjectName()}";
        (await Sform("account", "points", "get", "-p", unknown))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"No access point with key '{unknown}' has been found.");
    }

    // ACC-PNT-03: points get by a Contributor fails with exit 2.
    // As exactly a Contributor (in <contributor project> of Target:RoleProjects, or a Contributor of the target project),
    // sform account points get -p <org>.<contributor project> ends with 2 and "You don't have administration permissions to
    // the access point '<org>.<contributor project>'." without listing any member.
    [Test]
    [TestCase(274)]
    [UserRole(Role.Contributor)]
    [Covers("account points get")]
    public async Task Points_GetAsContributor_Refused()
    {
        (await Sform("account", "points", "get", "-p", ProjectKey))
            .ShouldExitWith(CliExit.WrongInput)
            .ShouldContain($"You don't have administration permissions to the access point '{ProjectKey}'.")
            .ShouldNotContain("# Access point");
    }

    // ACC-PNT-04: points create creates a project the creator owns and makes it the default project.
    // As an Owner of Target:Organization, sform account points create in a terminal asks "Select existing organization or
    // create new" (the organization is picked by typing its name into the list) and "Name of your new project" (a
    // generated name) and ends with 0; sform.settings.json of the home then names the new project with the view default,
    // and sform account points get -p <org>.<new project> lists the leased user as its Owner. The project stays.
    [Test]
    [TestCase(295)]
    [UserRole(Role.Owner)]
    [Terminal]
    [DisposableProject]
    [Covers("account points create")]
    public async Task Points_Create_CreatesOwnedProject()
    {
        var name = CliData.ProjectName();
        var key = $"{Target.Organization}.{name}";
        Scope.Properties["disposableProject"] = key;

        await using (var terminal = await StartSformInTerminal("account", "points", "create"))
        {
            await terminal.ExpectAsync("Select existing organization or create new");
            await terminal.SendAsync(Target.Organization);
            await terminal.ExpectAsync(Target.Organization);
            await terminal.SendKeyAsync(TerminalKey.Enter);
            await terminal.ExpectAsync("Name of your new project");
            await terminal.SendLineAsync(name);
            (await terminal.WaitForExitAsync()).ShouldSucceed();
        }

        await Assert.That(Home.AccessSetting("organizationName")).IsEqualTo(Target.Organization);
        await Assert.That(Home.AccessSetting("projectName")).IsEqualTo(name);
        await Assert.That(Home.AccessSetting("viewName")).IsEqualTo("default");

        var accountId = await AccountIdAsync();
        var get = (await Sform("account", "points", "get", "-p", key)).ShouldSucceed();
        await Assert.That(AccessPointOutput.RoleOf(get, accountId)).IsEqualTo(PlatformRoles.Owner);
    }

    // ACC-PNT-05: points grant gives each role, Reader to Owner, to another pool user.
    // In a disposable project of the leased Owner, with the account id of a second pool user: sform account points grant
    // <id> -p <org>.<project> -r Reader, then --point … --role Contributor, then -r ContributorWithSecrets, -r Administrator
    // and -r Owner; each prints "Access granted to <id> with role <role> to <org>.<project>." and sform account points get -p
    // <org>.<project> then shows the second user with that role (each grant raises the role).
    [Test]
    [TestCase(296)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("account points grant", "arg0", "-p", "--point", "-r", "--role", "--role=Reader", "--role=Contributor", "--role=ContributorWithSecrets", "--role=Administrator", "--role=Owner")]
    public async Task Points_Grant_EachRole()
    {
        var project = await CreateDisposableProjectAsync();
        var second = await AsAnotherUserAsync(UserRequirement.Any);
        var id = await second.AccountIdAsync();

        foreach (var role in Roles)
        {
            string[] target = role == PlatformRoles.Contributor ? ["--point", project, "--role", role] : ["-p", project, "-r", role];
            (await Sform(["account", "points", "grant", id, .. target]))
                .ShouldSucceed()
                .ShouldContain($"Access granted to {id} with role {role} to {project}.");

            await Assert.That(await MemberRoleAsync(project, id)).IsEqualTo(role);
        }
    }

    // ACC-PNT-06: points revoke -r removes a member of that role, and revoke without -r removes a Reader.
    // In a disposable project of the leased Owner, with the account id of a second pool user: for each role a grant of it,
    // then sform account points revoke <id> -p <org>.<project> with -r Reader, --role Contributor, -r
    // ContributorWithSecrets, -r Administrator and -r Owner; each prints "Access revoked to <id> with role <role> to
    // <org>.<project>." and sform account points get -p <org>.<project> no longer lists the user. Then a grant of Reader and
    // sform account points revoke <id> --point <org>.<project> without -r, which sends Reader and removes the Reader.
    [Test]
    [TestCase(297)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("account points revoke", "arg0", "-p", "--point", "-r", "--role", "--role=Reader", "--role=Contributor", "--role=ContributorWithSecrets", "--role=Administrator", "--role=Owner")]
    public async Task Points_Revoke_EachRole()
    {
        var project = await CreateDisposableProjectAsync();
        var second = await AsAnotherUserAsync(UserRequirement.Any);
        var id = await second.AccountIdAsync();

        foreach (var role in Roles)
        {
            await GrantAsync(id, role, project);
            await Assert.That(await MemberRoleAsync(project, id)).IsEqualTo(role);

            var option = role == PlatformRoles.Contributor ? "--role" : "-r";
            (await Sform("account", "points", "revoke", id, "-p", project, option, role))
                .ShouldSucceed()
                .ShouldContain($"Access revoked to {id} with role {role} to {project}.");

            await Assert.That(await MemberRoleAsync(project, id)).IsNull();
        }

        await GrantAsync(id, PlatformRoles.Reader, project);
        (await Sform("account", "points", "revoke", id, "--point", project))
            .ShouldSucceed()
            .ShouldContain($"Access revoked to {id} with role {PlatformRoles.Reader} to {project}.");
        await Assert.That(await MemberRoleAsync(project, id)).IsNull();
    }

    // ACC-PNT-07: An Administrator cannot grant Owner, and an unknown or missing role grants Contributor.
    // In a disposable project of the leased Owner: a second pool user granted Administrator there, and the account id of a
    // third. As the second user, sform account points grant <id3> -p <org>.<project> -r Owner is refused with 44, "The
    // account '<id2>' can't manage the role Owner on '<org>.<project>'." and "Error code: AccessDenied", and <id3> gets no
    // role; -r Superuser grants Contributor ("Access granted to <id3> with role Contributor to <org>.<project>."). The Owner
    // revokes that Contributor, and the second user's grant without -r grants Contributor again, as sform account points get
    // -p <org>.<project> shows.
    [Test]
    [TestCase(298)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    [Covers("account points grant", "-r")]
    public async Task Points_GrantAsAdministrator_RoleRules()
    {
        var project = await CreateDisposableProjectAsync();
        var administrator = await AsGrantedUserAsync(PlatformRoles.Administrator);
        var id2 = await administrator.AccountIdAsync();
        var third = await AsAnotherUserAsync(UserRequirement.Any);
        var id3 = await third.AccountIdAsync();

        (await administrator.Sform("account", "points", "grant", id3, "-p", project, "-r", PlatformRoles.Owner))
            .ShouldExitWith(CliExit.Unauthorized)
            .ShouldContain($"The account '{id2}' can't manage the role Owner on '{project}'.")
            .ShouldContain("Error code: AccessDenied")
            .ShouldNotContain("Access granted");
        await Assert.That(await MemberRoleAsync(project, id3)).IsNull();

        (await administrator.Sform("account", "points", "grant", id3, "-p", project, "-r", "Superuser"))
            .ShouldSucceed()
            .ShouldContain($"Access granted to {id3} with role Contributor to {project}.");
        await Assert.That(await MemberRoleAsync(project, id3)).IsEqualTo(PlatformRoles.Contributor);

        (await Sform("account", "points", "revoke", id3, "-p", project, "-r", PlatformRoles.Contributor)).ShouldSucceed();
        await Assert.That(await MemberRoleAsync(project, id3)).IsNull();

        (await administrator.Sform("account", "points", "grant", id3, "-p", project))
            .ShouldSucceed()
            .ShouldContain($"Access granted to {id3} with role Contributor to {project}.");
        await Assert.That(await MemberRoleAsync(project, id3)).IsEqualTo(PlatformRoles.Contributor);
    }

    // The member's role in the project as sform account points get -p <project> shows it to the leased user (an Owner of
    // the project); null when the member is not listed.
    private async Task<string?> MemberRoleAsync(string project, string accountId)
    {
        var get = (await Sform(new SformRunOptions { Label = $"the members of {project}" }, "account", "points", "get", "-p", project)).ShouldSucceed();
        return AccessPointOutput.RoleOf(get, accountId);
    }
}
