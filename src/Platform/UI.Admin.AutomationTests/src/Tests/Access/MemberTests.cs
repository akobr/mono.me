using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Access;

// Members: the project Members page with its members table, the locked own role and the Owner-only controls, the
// roles legend and the highlight of a member, role changes and removal, and no access below Administrator.
[Area(Area.Access)]
public sealed partial class MemberTests : PlatformTest
{
    // ACC-MEM-01: The project Members page lists the members, locks the own role and highlights a member.
    // As an Administrator or Owner, the project's Members page has its sections, Roles and Invite; the own row ("you")
    // has the own account id and a locked role select; the legend describes the five roles; ?highlight=<account id>
    // marks exactly the own row.
    [Test]
    [TestCase(50)]
    [UserRole(Role.Admin)]
    public async Task Members_ProjectPage_LocksOwnRoleAndHighlights()
    {
        var members = new MembersPage(App, Routes);
        await members.OpenAsync();
        var name = await App.SignedInNameAsync();

        await Expect(members.InvitationsHeading).ToBeVisibleAsync();
        await Expect(members.Intro).ToBeVisibleAsync();
        await Expect(members.Legend.Button).ToBeVisibleAsync();
        await Expect(members.Invite.OpenButton).ToBeVisibleAsync();

        var table = members.Members;
        foreach (var column in new[] { "Name", "User", "Role" })
        {
            await Expect(table.ColumnHeader(column)).ToBeVisibleAsync();
        }

        await Expect(table.OwnRow).ToHaveCountAsync(1);
        var accountId = await table.OwnAccountIdAsync();
        await Expect(table.OwnRow.GetByText(accountId, new() { Exact = true })).ToBeVisibleAsync();

        // A deployed target's account may have no name, and then the cell shows '–'.
        await Expect(table.OwnNameCell).ToContainTextAsync(Target.IsMock ? new Regex(Regex.Escape(name)) : new Regex($"{Regex.Escape(name)}|–"));
        await Expect(table.OwnRoleSelect).ToBeDisabledAsync();
        await Expect(table.OwnRoleSelect).ToHaveTextAsync(AdminRole());

        await members.Legend.OpenAsync();
        await Expect(members.Legend.Roles).ToHaveTextAsync(RoleLegend.RoleLabels);
        await Expect(members.Legend.Descriptions).ToHaveTextAsync(RoleLegend.RoleDescriptions);
        await Expect(members.Legend.Root.GetByText("Manages members, invitations, shared certificates, the machine policy and views.", new() { Exact = true })).ToBeVisibleAsync();
        await members.Legend.CloseAsync();

        await members.OpenAsync(highlight: accountId);
        await Expect(table.OwnRow).ToHaveAttributeAsync("aria-current", "true");
        await Expect(table.HighlightedRows).ToHaveCountAsync(1);
    }

    // ACC-MEM-02: The only Owner of a new project can be neither demoted nor removed.
    // The creator of a new project is its only member and Owner: the last-Owner rule locks both the role select and
    // Remove of the own row, and the project has no invitations.
    [Test]
    [TestCase(52)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task Members_OnlyOwner_CannotBeDemotedOrRemoved()
    {
        var project = await CreateDisposableProjectAsync();

        var members = new MembersPage(App, project);
        await members.OpenAsync();
        var table = members.Members;
        await Expect(table.Rows).ToHaveCountAsync(1);
        await Expect(table.OwnRow).ToHaveCountAsync(1);
        await Expect(table.OwnRoleSelect).ToHaveTextAsync("Owner");
        await Expect(table.OwnRoleSelect).ToBeDisabledAsync();
        await Expect(table.OwnRemoveButton).ToBeDisabledAsync();
        await Expect(members.Invitations.NoInvitations).ToBeVisibleAsync();
    }

    // ACC-MEM-03: An Owner changes the role of a member and removes another member.
    // In the mock's acme/billing an Owner makes Grace Hopper an Administrator, which a reload keeps, and removes Alan
    // Turing after cancelling the first confirmation.
    [Test]
    [TestCase(54)]
    [UserRole(Role.Owner)]
    [MockOnly("the seeded members Grace Hopper and Alan Turing of acme/billing")]
    public async Task Members_ChangeRoleAndRemove_UpdatesTable()
    {
        var billing = AppRoutes.Parse("acme/billing");
        var members = new MembersPage(App, billing);
        await members.OpenAsync();
        var table = members.Members;
        await Expect(table.Rows).ToHaveTextAsync([new Regex("Ada Lovelace"), new Regex("Alan Turing"), new Regex("Grace Hopper")]);
        await Expect(table.RoleSelect("Grace Hopper")).ToHaveTextAsync("Contributor");
        await Expect(table.RoleSelect("Alan Turing")).ToHaveTextAsync("Reader");

        await table.RoleSelect("Grace Hopper").ClickAsync();
        await Expect(table.RoleOptions).ToHaveTextAsync(["Reader", "Contributor", "ContributorWithSecrets", "Administrator", "Owner"]);
        await table.RoleOption("Administrator").ClickAsync();
        await Expect(App.Toast("Grace Hopper is now Administrator")).ToBeVisibleAsync();
        await Expect(table.RoleSelect("Grace Hopper")).ToHaveTextAsync("Administrator");

        // The same tab keeps its mock data; a new scenario would reset it.
        await members.OpenAsync();
        await Expect(table.RoleSelect("Grace Hopper")).ToHaveTextAsync("Administrator");

        var remove = table.RemoveDialog("Alan Turing");
        await table.RemoveButton("Alan Turing").ClickAsync();
        await remove.ExpectOpenAsync();
        await Expect(remove.Root).ToHaveAccessibleDescriptionAsync("They lose their Reader role on acme.billing.");
        await remove.CancelAsync();
        await Expect(table.Row("Alan Turing")).ToHaveCountAsync(1);

        await table.RemoveButton("Alan Turing").ClickAsync();
        await remove.ConfirmAsync("Remove");
        await Expect(App.Toast("Alan Turing removed")).ToBeVisibleAsync();
        await Expect(table.Row("Alan Turing")).ToHaveCountAsync(0);
    }

    // ACC-MEM-04: Below Administrator the Members page offers no Invite and no access.
    // Exactly a Contributor of the project (Target:RoleProjects) reaches the Members page, but both sections say "No
    // access" with Retry, there is no Invite, and the roles legend still opens.
    [Test]
    [TestCase(55)]
    [UserRole(Role.Contributor)]
    public async Task Members_Contributor_ShowsNoAccess()
    {
        var members = new MembersPage(App, Routes);
        await members.OpenAsync();
        await Expect(members.InvitationsHeading).ToBeVisibleAsync();
        await Expect(members.Legend.Button).ToBeVisibleAsync();

        await Expect(members.Members.NoAccess).ToBeVisibleAsync();
        await Expect(members.Members.RetryButton).ToBeVisibleAsync();
        await Expect(members.Invitations.NoAccess).ToBeVisibleAsync();
        await Expect(members.Invitations.RetryButton).ToBeVisibleAsync();

        // Asserted once the page settled with its "No access" states.
        await Expect(members.Invite.OpenButton).ToHaveCountAsync(0);

        await members.Legend.OpenAsync();
        await Expect(members.Legend.Roles).ToHaveTextAsync(RoleLegend.RoleLabels);
    }

    // The own role of an Administrator or Owner.
    [GeneratedRegex("^(Administrator|Owner)$")]
    private static partial Regex AdminRole();
}
