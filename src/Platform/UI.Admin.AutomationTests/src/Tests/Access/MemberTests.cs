using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Access;

// Members: the project Members page with its members table, the locked own role and the Owner-only controls, the
// roles legend and the highlight of a member, role changes and removal, no access below Administrator, and leaving a
// project from its Settings, which every member but the only Owner may.
[Area(Area.Access)]
public sealed partial class MemberTests : PlatformTest
{
    // The exhibition of the mock: the project exhibition/exhibition of Grace Hopper, with a small catalog, which any
    // user may join as a Reader.
    private static readonly AppRoutes Exhibition = AppRoutes.Parse("exhibition/exhibition");

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

    // ACC-MEM-05: A Contributor leaves a project from its Settings and loses access to it.
    // Exactly a Contributor of the project (Target:RoleProjects; the mock's acme/shipping) finds "Leave project" in the
    // footer of the Project card. Its confirmation names the role that goes and that only a new invitation brings it
    // back; Cancel keeps the membership. A confirmed leave opens /orgs with a toast, the organization's card lists the
    // project no more, and its address shows "You have no access here". Mock only: a leave drops the pool user's
    // membership for good on a deployed target.
    [Test]
    [TestCase(512)]
    [UserRole(Role.Contributor)]
    [MockOnly("leaving drops the pool user's membership of the project for good on a deployed target")]
    public async Task Members_ContributorLeavesProject_LosesAccess()
    {
        var key = $"{Routes.Organization}.{Routes.Project}";
        var settings = new ProjectSettingsPage(App, Routes);
        await settings.OpenAsync();
        await Expect(settings.ProjectValue("Your role")).ToHaveTextAsync("Contributor");
        await Expect(settings.LeaveProjectText).ToBeVisibleAsync();
        await Expect(settings.LeaveProjectButton).ToBeEnabledAsync();

        var leave = await settings.OpenLeaveDialogAsync();
        await Expect(leave.Root).ToHaveAccessibleDescriptionAsync($"You lose your Contributor role on {key}. To come back, you need a new invitation.");
        await leave.CancelAsync();
        await Expect(settings.ProjectValue("Your role")).ToHaveTextAsync("Contributor");

        await settings.OpenLeaveDialogAsync();
        await settings.ConfirmLeaveAsync();
        await Expect(App.Toast($"You left {key}")).ToBeVisibleAsync();

        // The organization stays, with the user's other roles (the mock: Owner of acme and of acme/billing); no link of
        // the page, the navigation's included, leads into the project.
        var organizations = new OrganizationsPage(App, Routes);
        await Expect(organizations.Organization(Routes.Organization)).ToBeVisibleAsync();
        await Expect(organizations.LinksInto(Routes.Organization, Routes.Project)).ToHaveCountAsync(0);

        await App.GotoAsync(Routes.Annotations);
        await App.WaitForReadyAsync();
        await Expect(App.NoAccessHeading).ToBeVisibleAsync();
    }

    // ACC-MEM-06: A visitor leaves the exhibition from its Settings, and the Organizations page offers it again.
    // In the mock's default scenario the user joins the exhibition on /orgs and leaves it again on its Settings as a
    // Reader. The confirmation says that the exhibition can be joined again from Organizations; the leave opens /orgs
    // with a toast, without the exhibition's organization and with the offer of the exhibition back.
    [Test]
    [TestCase(513)]
    [MockOnly("the exhibition of the mock, which the default scenario's user is no member of")]
    public async Task Members_VisitorLeavesExhibition_OffersItAgain()
    {
        var key = $"{Exhibition.Organization}.{Exhibition.Project}";
        var organizations = new OrganizationsPage(App, Routes);
        await organizations.OpenAsync();
        await organizations.JoinExhibitionAsync();
        await App.ExpectAtAsync(Exhibition.Annotations);

        var settings = new ProjectSettingsPage(App, Exhibition);
        await settings.OpenAsync();
        await Expect(settings.ProjectValue("Your role")).ToHaveTextAsync("Reader");
        await Expect(settings.LeaveProjectButton).ToBeEnabledAsync();
        var leave = await settings.OpenLeaveDialogAsync();
        await Expect(leave.Root).ToHaveAccessibleDescriptionAsync($"You lose your Reader role on {key}. You can join the exhibition again from Organizations.");

        await settings.ConfirmLeaveAsync();
        await Expect(App.Toast($"You left {key}")).ToBeVisibleAsync();
        await Expect(organizations.Organization(Exhibition.Organization)).ToHaveCountAsync(0);
        await Expect(organizations.JoinExhibitionButton).ToBeVisibleAsync();
    }

    // ACC-MEM-07: The only Owner of a project cannot leave it.
    // The creator of a new project is its only member and Owner: the footer of the Project card on its Settings has a
    // disabled "Leave project" and says that another member must become an Owner first.
    [Test]
    [TestCase(514)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task Members_OnlyOwner_CannotLeave()
    {
        var project = await CreateDisposableProjectAsync();

        var settings = new ProjectSettingsPage(App, project);
        await settings.OpenAsync();
        await Expect(settings.ProjectValue("Your role")).ToHaveTextAsync("Owner");

        // The rule needs the project's members, which the page reads after it showed: the text comes with them.
        await Expect(settings.OnlyOwnerText).ToBeVisibleAsync();
        await Expect(settings.LeaveProjectButton).ToBeDisabledAsync();
        await Expect(settings.LeaveProjectText).ToHaveCountAsync(0);
    }

    // The own role of an Administrator or Owner.
    [GeneratedRegex("^(Administrator|Owner)$")]
    private static partial Regex AdminRole();
}
