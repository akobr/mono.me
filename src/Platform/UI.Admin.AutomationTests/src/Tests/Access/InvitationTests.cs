using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Access;

// Invitations: the Invite dialog of the project and of the organization (typed and pasted lists, one role and expiry,
// validation, already pending addresses) and the Invitations table's Resend, Copy link and Revoke. Every address is a
// generated sub-address of Target:EmailDomain; what a test leaves pending, its cleanup revokes (invitations cannot be
// deleted, so they stay listed as Revoked).
[Area(Area.Access)]
public sealed partial class InvitationTests : PlatformTest
{
    // The addresses a test invited to the project and to the organization, revoked in CleanUpAsync while pending.
    private readonly List<string> projectInvitations = [];
    private readonly List<string> organizationInvitations = [];

    // ACC-INV-01: Invite one person, resend and copy the link, then revoke the invitation.
    // One typed address gets a Pending Reader invitation from the signed-in user; Resend keeps it pending, Copy link
    // copies its landing link, a cancelled Revoke changes nothing, and Revoke leaves it Revoked without actions.
    [Test]
    [TestCase(77)]
    [UserRole(Role.Admin)]
    public async Task Invitations_InviteResendRevoke_EndsRevoked()
    {
        var email = PlatformData.Email();
        await InstallClipboardRecorderAsync();

        var members = new MembersPage(App, Routes);
        await members.OpenAsync();
        var name = await App.SignedInNameAsync();

        var invite = members.Invite;
        await invite.OpenAsync();
        await Expect(invite.Root).ToHaveAccessibleDescriptionAsync($"To {Routes.Organization}.{Routes.Project}. Everyone gets the same role and expiry.");
        await Expect(invite.RoleSelect).ToHaveTextAsync("Reader");
        await Expect(invite.ExpiresField).ToHaveValueAsync("7");

        await invite.AddEmailAsync(email);
        projectInvitations.Add(email);
        await invite.SendAsync();
        await Expect(App.Toast("1 invitation sent")).ToBeVisibleAsync();
        await Expect(invite.Root).ToBeHiddenAsync();

        var invitations = members.Invitations;
        var row = invitations.Row(email);
        await Expect(row).ToHaveCountAsync(1);
        await Expect(invitations.Cell(row, "Role")).ToHaveTextAsync("Reader");
        await Expect(invitations.Cell(row, "Status")).ToHaveTextAsync("Pending");

        // A deployed target names the token's user, or the account id.
        await Expect(invitations.Cell(row, "Invited by")).ToHaveTextAsync(Target.IsMock ? new Regex($"^{Regex.Escape(name)}$") : NotBlank());
        foreach (var action in new[] { "Copy link", "Resend", "Revoke" })
        {
            await Expect(invitations.Action(row, action)).ToBeVisibleAsync();
        }

        await invitations.Action(row, "Resend").ClickAsync();
        await Expect(App.Toast("Invitation resent")).ToBeVisibleAsync();
        await Expect(invitations.Cell(row, "Status")).ToHaveTextAsync("Pending");

        await invitations.Action(row, "Copy link").ClickAsync();
        await Expect(App.Toast("Invitation link copied")).ToBeVisibleAsync();
        var origin = new Uri(Page.Url).GetLeftPart(UriPartial.Authority);
        await ExpectCopiedAsync(new Regex($@"^{Regex.Escape(origin)}/invitations/accept\?id=.+$"));

        var revoke = invitations.RevokeDialog(email);
        await invitations.Action(row, "Revoke").ClickAsync();
        await revoke.ExpectOpenAsync();
        await Expect(revoke.Root).ToHaveAccessibleDescriptionAsync("The invitation link stops working.");
        await revoke.CancelAsync();
        await Expect(invitations.Cell(row, "Status")).ToHaveTextAsync("Pending");

        await invitations.Action(row, "Revoke").ClickAsync();
        await revoke.ConfirmAsync("Revoke");
        await Expect(App.Toast("Invitation revoked")).ToBeVisibleAsync();
        await Expect(invitations.Cell(row, "Status")).ToHaveTextAsync("Revoked");
        projectInvitations.Remove(email);
        foreach (var action in new[] { "Copy link", "Resend", "Revoke" })
        {
            await Expect(invitations.Action(row, action)).ToHaveCountAsync(0);
        }
    }

    // ACC-INV-02: Several typed addresses get one invitation each, with one role and expiry.
    // A typed space or comma commits the address before it (in lower case), and Send commits the rest; an Owner picks
    // Contributor among the five roles and 14 days, and every address gets a Pending Contributor invitation with them.
    [Test]
    [TestCase(79)]
    [UserRole(Role.Owner)]
    public async Task Invitations_TypedList_SendsOneRoleAndExpiry()
    {
        string[] emails = [PlatformData.Email(), PlatformData.Email(), PlatformData.Email()];
        await InstallRequestRecorderAsync();

        var members = new MembersPage(App, Routes);
        await members.OpenAsync();
        var invite = members.Invite;
        await invite.OpenAsync();

        await invite.EmailsInput.PressSequentiallyAsync($"{emails[0].ToUpperInvariant()} {emails[1]},{emails[2]}");
        await Expect(invite.Tags).ToHaveCountAsync(2);
        await Expect(invite.Tag(emails[0])).ToBeVisibleAsync();
        await Expect(invite.Tag(emails[1])).ToBeVisibleAsync();
        await Expect(invite.EmailsInput).ToHaveValueAsync(emails[2]);

        await invite.RoleSelect.ClickAsync();
        await Expect(invite.RoleOptions).ToHaveTextAsync(["Reader", "Contributor", "ContributorWithSecrets", "Administrator", "Owner"]);
        await Page.GetByRole(AriaRole.Option, new() { Name = "Contributor", Exact = true }).ClickAsync();
        await Expect(invite.RoleSelect).ToHaveTextAsync("Contributor");
        await invite.ExpiresField.FillAsync("14");
        await Expect(invite.ExpiresField).ToHaveValueAsync("14");
        await Expect(invite.EmailsInput).ToHaveValueAsync(emails[2]);

        projectInvitations.AddRange(emails);
        await invite.SendAsync();
        await Expect(App.Toast("3 invitations sent")).ToBeVisibleAsync();
        await Expect(invite.Root).ToBeHiddenAsync();

        var invitations = members.Invitations;
        foreach (var email in emails)
        {
            var row = invitations.Row(email);
            await Expect(row).ToHaveCountAsync(1);
            await Expect(invitations.Cell(row, "Role")).ToHaveTextAsync("Contributor");
            await Expect(invitations.Cell(row, "Status")).ToHaveTextAsync("Pending");
        }

        // The relative expiry rounds; the requests show the one expiry each got.
        var sent = (await RecordedRequestsAsync())
            .Where(request => request.Method == "POST" && InvitationsEndpoint().IsMatch(request.Url))
            .ToList();
        await Assert.That(sent.Count).IsEqualTo(emails.Length);
        foreach (var request in sent)
        {
            await Assert.That(request.Body ?? string.Empty).Matches(ContributorFor14Days());
        }
    }

    // ACC-INV-03: A list pasted from a mail client invites people to the organization.
    // On the organization's Members page an Owner of the organization pastes a list with display names, a quoted name
    // with a comma and a repeated address: it becomes exactly the two bare addresses, each invited as Reader.
    [Test]
    [TestCase(80)]
    [UserRole(Role.Owner)]
    public async Task Invitations_PastedListToOrganization_SendsEach()
    {
        var (first, second) = (PlatformData.Email(), PlatformData.Email());

        var members = new OrganizationMembersPage(App, Routes);
        await members.OpenAsync();
        await Expect(members.Intro).ToBeVisibleAsync();
        await Expect(members.Members.OwnRow).ToHaveCountAsync(1);
        await Expect(members.InvitationsHeading).ToBeVisibleAsync();

        var invite = members.Invite;
        await invite.OpenAsync();
        await Expect(invite.Root).ToHaveAccessibleDescriptionAsync($"To {Routes.Organization}. Everyone gets the same role and expiry.");

        await invite.PasteAsync($"First One <{first}>, \"Last, First\" <{second}>;\n{first}");
        await Expect(invite.Tags).ToHaveCountAsync(2);
        await Expect(invite.Tag(first)).ToBeVisibleAsync();
        await Expect(invite.Tag(second)).ToBeVisibleAsync();

        organizationInvitations.AddRange([first, second]);
        await invite.SendAsync();
        await Expect(App.Toast("2 invitations sent")).ToBeVisibleAsync();
        await Expect(invite.Root).ToBeHiddenAsync();

        var invitations = members.Invitations;
        foreach (var email in new[] { first, second })
        {
            var row = invitations.Row(email);
            await Expect(row).ToHaveCountAsync(1);
            await Expect(invitations.Cell(row, "Role")).ToHaveTextAsync("Reader");
            await Expect(invitations.Cell(row, "Status")).ToHaveTextAsync("Pending");
        }
    }

    // ACC-INV-04: Missing, invalid and already invited addresses are refused.
    // Send with nothing typed asks for an address, an invalid tag is struck through and disables Send, and reopening
    // clears both; an address with a pending invitation is refused and stays as the only tag while the other is sent.
    [Test]
    [TestCase(82)]
    [UserRole(Role.Admin)]
    public async Task Invitations_InvalidAndPendingAddresses_AreRefused()
    {
        var (first, second) = (PlatformData.Email(), PlatformData.Email());

        var members = new MembersPage(App, Routes);
        await members.OpenAsync();
        var invite = members.Invite;
        await invite.OpenAsync();

        await invite.SendButton.ClickAsync();
        await Expect(invite.EmailsError).ToHaveTextAsync("Enter at least one email address.");
        await Expect(invite.EmailsInput).ToBeFocusedAsync();
        await Expect(invite.Root).ToBeVisibleAsync();

        await invite.EmailsInput.PressSequentiallyAsync("grace.example.com ");
        await Expect(invite.Tag("grace.example.com")).ToBeVisibleAsync();
        await Expect(invite.TagText("grace.example.com")).ToHaveCSSAsync("text-decoration-line", "line-through");
        await Expect(invite.EmailsError).ToHaveTextAsync("Not an email: grace.example.com");
        await Expect(invite.SendButton).ToBeDisabledAsync();

        await invite.CancelButton.ClickAsync();
        await Expect(invite.Root).ToBeHiddenAsync();
        await invite.OpenAsync();
        await Expect(invite.Tags).ToHaveCountAsync(0);
        await Expect(invite.EmailsError).ToHaveCountAsync(0);

        projectInvitations.AddRange([first, second]);
        await invite.AddEmailAsync(first);
        await invite.SendAsync();
        await Expect(App.Toast("1 invitation sent")).ToBeVisibleAsync();
        await Expect(invite.Root).ToBeHiddenAsync();

        // The same title follows: wait for the first toast to close.
        await Expect(App.Toast("1 invitation sent")).ToBeHiddenAsync();
        await invite.OpenAsync();
        await invite.AddEmailAsync(first);
        await invite.AddEmailAsync(second);
        await invite.SendAsync();
        await Expect(App.Toast("1 invitation sent")).ToBeVisibleAsync();
        await Expect(invite.Failures).ToHaveTextAsync($"{first}: A pending invitation already exists.");
        await Expect(invite.Tags).ToHaveCountAsync(1);
        await Expect(invite.Tag(first)).ToBeVisibleAsync();

        await invite.CancelButton.ClickAsync();
        await Expect(invite.Root).ToBeHiddenAsync();
        var invitations = members.Invitations;
        foreach (var email in new[] { first, second })
        {
            await Expect(invitations.Row(email)).ToHaveCountAsync(1);
            await Expect(invitations.Cell(invitations.Row(email), "Status")).ToHaveTextAsync("Pending");
        }

        await Expect(invitations.Row("grace.example.com")).ToHaveCountAsync(0);
    }

    // Revokes what the test left pending: invitations cannot be deleted, and a pending one would block an invitation of
    // the same address.
    protected override async Task CleanUpAsync()
    {
        if (projectInvitations.Count > 0)
        {
            var members = new MembersPage(App, Routes);
            await members.OpenAsync();
            foreach (var email in projectInvitations)
            {
                await members.Invitations.RevokePendingAsync(email);
            }
        }

        if (organizationInvitations.Count > 0)
        {
            var members = new OrganizationMembersPage(App, Routes);
            await members.OpenAsync();
            foreach (var email in organizationInvitations)
            {
                await members.Invitations.RevokePendingAsync(email);
            }
        }
    }

    // Waits until the page copied something and checks that the last copied text matches.
    private async Task ExpectCopiedAsync(Regex expected)
    {
        await Page.WaitForFunctionAsync("() => (window.__copied ?? []).length > 0", null, new() { PollingInterval = 100 });
        await Assert.That(await CopiedTextAsync() ?? string.Empty).Matches(expected);
    }

    [GeneratedRegex(@"\S")]
    private static partial Regex NotBlank();

    // POST …/access/points/<key>/invitations, the creation of one invitation.
    [GeneratedRegex(@"/access/points/[^/]+/invitations$")]
    private static partial Regex InvitationsEndpoint();

    [GeneratedRegex(@"(?=.*""Role"":\s*""Contributor"")(?=.*""ExpiresInDays"":\s*14\b)", RegexOptions.Singleline)]
    private static partial Regex ContributorFor14Days();
}
