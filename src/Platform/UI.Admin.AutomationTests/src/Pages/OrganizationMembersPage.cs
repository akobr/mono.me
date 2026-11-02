using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// /<org>/members (the sidebar's organization "Members", Administrator and up on the organization itself): the section
// "Organization members" with its intro and the members, and a section "Invitations" (without an id) with the
// organization's invitations. The top bar has "Roles" and, for an Administrator or Owner of the organization, "Invite";
// a project role grants neither. The section headings are upper case only through CSS.
public sealed partial class OrganizationMembersPage(App app, AppRoutes routes)
{
    public ILocator MembersHeading => app.Page.GetByRole(AriaRole.Heading, new() { Name = "Organization members", Exact = true, Level = 2 });

    public ILocator InvitationsHeading => app.Page.GetByRole(AriaRole.Heading, new() { Name = "Invitations", Exact = true, Level = 2 });

    public ILocator Intro => MembersSection.GetByText(
        $"Roles on {routes.Organization} itself; they do not grant access to its projects.",
        new() { Exact = true });

    public ILocator MembersSection => app.Page.Locator("section").Filter(new() { Has = MembersHeading });

    public ILocator InvitationsSection => app.Page.Locator("section").Filter(new() { Has = InvitationsHeading });

    public MembersTable Members => new(app, MembersSection);

    public InvitationsTable Invitations => new(app, InvitationsSection);

    public RoleLegend Legend => new(app);

    public InviteDialog Invite => new(app);

    public async Task OpenAsync()
    {
        await app.GotoAsync(routes.OrganizationMembers);
        await app.WaitForReadyAsync();
        await Expect(MembersHeading).ToBeVisibleAsync();
    }
}
