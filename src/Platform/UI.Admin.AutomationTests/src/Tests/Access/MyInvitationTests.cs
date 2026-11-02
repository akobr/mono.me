using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Access;

// My Invitations: the signed-in user's /invitations with Decline and Accept of a self-invitation (the API invites any
// address, also the inviter's own, and an accepted invitation keeps a higher role), an invited user without an account,
// and the public invitation landing for a signed-out visitor.
[Area(Area.Access)]
public sealed partial class MyInvitationTests : PlatformTest
{
    // The own e-mail once the test invited it, until the invitation was declined or accepted: CleanUpAsync revokes a
    // pending self-invitation.
    private readonly HashSet<string> pendingSelfInvitations = new(StringComparer.OrdinalIgnoreCase);

    // ACC-MYI-01: A self-invitation is declined on Invitations, and a second one is accepted.
    // An Administrator or Owner invites the own e-mail to the project as Reader; the invitation landing of a signed-in
    // visitor opens /invitations, where Decline removes it (Declined on Members); a second one is accepted, opens the
    // project, and the own role stays above Reader.
    [Test]
    [TestCase(104)]
    [UserRole(Role.Admin)]
    public async Task MyInvitations_SelfInvitation_DeclineThenAccept()
    {
        var key = $"{Routes.Organization}.{Routes.Project}";
        var mine = new MyInvitationsPage(App, Routes);
        var members = new MembersPage(App, Routes);

        await mine.OpenAsync();
        var email = await mine.SignedInEmailAsync();
        if (!Target.IsMock && await mine.Entry(key).CountAsync() > 0)
        {
            // A leftover of an aborted run on a deployed target.
            await members.OpenAsync();
            await members.Invitations.RevokePendingAsync(email);
            await mine.OpenAsync();
        }

        await Expect(mine.Intro).ToHaveTextAsync($"Organizations and projects that invited {email}.");
        await Expect(mine.Entry(key)).ToHaveCountAsync(0);
        await Expect(mine.GoToMyOrganizationsLink).ToBeVisibleAsync();

        await members.OpenAsync();
        var name = await App.SignedInNameAsync();
        await InviteAsReaderAsync(members, email);

        await App.GotoAsync(Routes.InvitationAccept(id: "x"));
        await App.ExpectAtAsync(Routes.MyInvitations);
        await mine.WaitForListAsync();
        var entry = mine.Entry(key);
        await Expect(entry).ToHaveCountAsync(1);
        await Expect(entry.GetByText("Reader", new() { Exact = true })).ToBeVisibleAsync();

        // A deployed target names the token's user, or the account id; the relative expiry rounds.
        var inviter = Target.IsMock ? Regex.Escape(name) : @"\S.*";
        await Expect(mine.InvitedBy(entry)).ToHaveTextAsync(InvitedBy(inviter));
        await Expect(mine.AcceptButton(entry)).ToBeVisibleAsync();
        await Expect(mine.DeclineButton(entry)).ToBeVisibleAsync();

        await mine.DeclineButton(entry).ClickAsync();
        await Expect(entry).ToHaveCountAsync(0);
        pendingSelfInvitations.Remove(email);

        await members.OpenAsync();
        await Expect(members.Invitations.Row(email, "Declined")).ToHaveCountAsync(1);
        await InviteAsReaderAsync(members, email);

        await mine.OpenAsync();
        await Expect(entry).ToHaveCountAsync(1);
        await mine.AcceptButton(entry).ClickAsync();
        await Expect(App.Toast($"You joined {key}")).ToBeVisibleAsync();
        pendingSelfInvitations.Remove(email);
        await App.ExpectAtAsync((Routes with { View = AppRoutes.DefaultView }).Annotations);

        await members.OpenAsync();
        await Expect(members.Members.OwnRoleSelect).ToHaveTextAsync(AdminRole());
        await Expect(members.Invitations.Row(email, "Declined")).ToHaveCountAsync(1);
        await Expect(members.Invitations.Row(email, "Accepted")).ToHaveCountAsync(1);
    }

    // ACC-MYI-02: An invited user without an account joins through Invitations.
    // In the mock's invited scenario Ada Lovelace has no account and an invitation to acme.billing as Contributor, so
    // the app opens /invitations; Accept creates the account in the project, and /orgs lists it with that role.
    [Test]
    [TestCase(106)]
    [MockOnly("the invited scenario, a user without an account who has a pending invitation")]
    public async Task MyInvitations_InvitedNewUser_JoinsProject()
    {
        var billing = AppRoutes.Parse("acme/billing");

        // Not PlatformTest.App: its first navigation would add the default scenario.
        var app = new App(Page, "invited");
        await app.GotoAsync(billing.Home);
        await app.ExpectAtAsync(billing.MyInvitations);
        var mine = new MyInvitationsPage(app, billing);
        await mine.WaitForListAsync();
        var entry = mine.Entry("acme.billing");
        await Expect(entry.GetByText("Contributor", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(mine.InvitedBy(entry)).ToHaveTextAsync(InvitedBy(Regex.Escape("Grace Hopper")));
        await Expect(mine.CreateOwnOrganizationLink).ToBeVisibleAsync();

        await mine.AcceptButton(entry).ClickAsync();
        await Expect(app.Toast("You joined acme.billing")).ToBeVisibleAsync();
        await app.ExpectAtAsync(billing.Annotations);
        await app.WaitForReadyAsync();
        await Expect(app.UserMenu).ToHaveAccessibleNameAsync("User menu for Ada Lovelace");

        var organizations = new OrganizationsPage(app, billing);
        await organizations.OpenAsync();
        await Expect(organizations.Organization("acme")).ToBeVisibleAsync();
        await Expect(organizations.Project("acme", "billing")).ToHaveAccessibleNameAsync("billing Contributor");
    }

    // ACC-MYI-03: The invitation landing asks a signed-out visitor to continue to the sign-in.
    // Signed out, the landing of an invitation e-mail explains itself and Continue starts the sign-in with the
    // invitation: the mock signs in through /callback to /invitations, a deployed target leaves for AuthKit (where the
    // test ends signed out; the runtime signs the user in again for the next test).
    [Test]
    [TestCase(107)]
    public async Task MyInvitations_LandingSignedOut_ContinuesToSignIn()
    {
        await new OrganizationsPage(App, Routes).OpenAsync();
        await App.SignOutAsync();

        var landing = new InvitationLandingPage(App, Routes);
        await landing.OpenAsync(id: "inv_test", token: "tok_test");
        await Expect(landing.Intro).ToBeVisibleAsync();
        await Expect(landing.ContinueButton).ToBeVisibleAsync();

        var navigations = RecordMainFrameNavigations();
        await landing.ContinueButton.ClickAsync();
        if (!Target.IsMock)
        {
            // The hosted page may refuse the made-up token; the test only shows that the sign-in started.
            await WaitForHostedPageAsync();
            return;
        }

        await App.ExpectAtAsync(Routes.MyInvitations);
        await Expect(new MyInvitationsPage(App, Routes).Heading).ToBeVisibleAsync();
        await Assert.That(SignedInThroughCallback(navigations))
            .IsTrue()
            .Because($"the sign-in returns through /callback; the page went to {string.Join(", ", navigations)}");
    }

    // Revokes the self-invitation the test left pending: a pending one would block the next run's invitation.
    protected override async Task CleanUpAsync()
    {
        if (pendingSelfInvitations.Count > 0)
        {
            var members = new MembersPage(App, Routes);
            await members.OpenAsync();
            foreach (var email in pendingSelfInvitations)
            {
                await members.Invitations.RevokePendingAsync(email);
            }
        }
    }

    // Invites the e-mail with the dialog's default role, Reader.
    private async Task InviteAsReaderAsync(MembersPage members, string email)
    {
        var invite = members.Invite;
        await invite.OpenAsync();
        await Expect(invite.RoleSelect).ToHaveTextAsync("Reader");
        await invite.AddEmailAsync(email);
        pendingSelfInvitations.Add(email);
        await invite.SendAsync();
        await Expect(App.Toast("1 invitation sent")).ToBeVisibleAsync();
        await Expect(invite.Root).ToBeHiddenAsync();
    }

    // "Invited by <inviter> · expires <relative time>", whatever the time and the template's line breaks around it;
    // inviter is a pattern.
    private static Regex InvitedBy(string inviter)
    {
        return new Regex($@"^\s*Invited by {inviter} · expires\s+\S[\s\S]*$");
    }

    // The own role of an Administrator or Owner, above the invitation's Reader.
    [GeneratedRegex("^(Administrator|Owner)$")]
    private static partial Regex AdminRole();
}
