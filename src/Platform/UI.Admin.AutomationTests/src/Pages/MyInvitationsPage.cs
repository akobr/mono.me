using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// /invitations (bare layout, no user menu; also for a signed-in user without an account, whom the auth guard sends
// here when they have a pending invitation): "Invitations", "Organizations and projects that invited <e-mail>." with
// the e-mail of the sign-in (the mock: ada@example.com, not a pool placeholder's), and the pending invitations of that
// e-mail, each with its key (org or org.project), the role badge, "Invited by <name> · expires <relative>", "Accept"
// and "Decline"; else "No pending invitations". Accept toasts "You joined <key>" and opens the project's default view
// (an organization's overview); Decline removes the entry. The footer links "Go to my organizations", or "Create my own
// organization instead" without an account, and "Sign out".
public sealed partial class MyInvitationsPage(App app, AppRoutes routes)
{
    public ILocator Heading => app.Page.GetByRole(AriaRole.Heading, new() { Name = "Invitations", Exact = true, Level = 1 });

    public ILocator Intro => app.Page.GetByText("Organizations and projects that invited ");

    public ILocator NoPendingInvitations => app.Page.GetByRole(AriaRole.Heading, new() { Name = "No pending invitations", Exact = true });

    // The pending invitations, each with Accept and Decline (the toasts are list items too).
    public ILocator Entries => app.Page.GetByRole(AriaRole.Listitem).Filter(new() { Has = app.Page.GetByRole(AriaRole.Button, new() { Name = "Decline", Exact = true }) });

    public ILocator GoToMyOrganizationsLink => app.Page.GetByRole(AriaRole.Link, new() { Name = "Go to my organizations", Exact = true });

    public ILocator CreateOwnOrganizationLink => app.Page.GetByRole(AriaRole.Link, new() { Name = "Create my own organization instead", Exact = true });

    // The pending invitation of an organization ("acme") or a project ("acme.billing"), by its exact key.
    public ILocator Entry(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Entries.Filter(new() { Has = app.Page.GetByText(key, new() { Exact = true }) });
    }

    // "Invited by <name> · expires <relative time>" of an entry, the paragraph under its key; its text keeps the line
    // breaks of the template around the time.
    public ILocator InvitedBy(ILocator entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.GetByRole(AriaRole.Paragraph);
    }

    public ILocator AcceptButton(ILocator entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.GetByRole(AriaRole.Button, new() { Name = "Accept", Exact = true });
    }

    public ILocator DeclineButton(ILocator entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.GetByRole(AriaRole.Button, new() { Name = "Decline", Exact = true });
    }

    public async Task OpenAsync()
    {
        await app.GotoAsync(routes.MyInvitations);
        await WaitForListAsync();
    }

    // Waits until the page listed the pending invitations, or said it has none.
    public async Task WaitForListAsync()
    {
        await Expect(Heading).ToBeVisibleAsync();
        await Expect(Entries.First.Or(NoPendingInvitations)).ToBeVisibleAsync();
    }

    // The e-mail of the sign-in, from the intro.
    public async Task<string> SignedInEmailAsync()
    {
        await Expect(Intro).ToBeVisibleAsync();
        var text = (await Intro.InnerTextAsync()).Trim();
        var match = IntroText().Match(text);
        return match.Success
            ? match.Groups["email"].Value
            : throw new InvalidOperationException($"The intro of /invitations does not name the signed-in e-mail: '{text}'.");
    }

    [GeneratedRegex(@"^Organizations and projects that invited (?<email>\S+@\S+)\.$")]
    private static partial Regex IntroText();
}
