using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// /<org>/<project>/members (the sidebar's project "Members", Administrator and up): the section "Project members" with
// its intro and the members, and section#invitations (where the old …/invitations redirects) with the invitations.
// The top bar has "Roles" and, for an Administrator or Owner of the project, "Invite"; an organization role grants
// neither. The navbar's title is hidden below 1536 px, so the page is known by its section headings, whose upper case
// is only CSS. A Contributor or Reader reaches the page, and each section shows "No access".
public sealed partial class MembersPage(App app, AppRoutes routes)
{
    public ILocator MembersHeading => app.Page.GetByRole(AriaRole.Heading, new() { Name = "Project members", Exact = true, Level = 2 });

    public ILocator InvitationsHeading => InvitationsSection.GetByRole(AriaRole.Heading, new() { Name = "Invitations", Exact = true, Level = 2 });

    public ILocator Intro => MembersSection.GetByText(
        $"Members of {routes.Organization}.{routes.Project}. An organization role does not grant access to its projects; invite people to the project itself.",
        new() { Exact = true });

    public ILocator MembersSection => app.Page.Locator("section").Filter(new() { Has = MembersHeading });

    public ILocator InvitationsSection => app.Page.Locator("section#invitations");

    public MembersTable Members => new(app, MembersSection);

    public InvitationsTable Invitations => new(app, InvitationsSection);

    public RoleLegend Legend => new(app);

    public InviteDialog Invite => new(app);

    // highlight: the account id to mark (?highlight=<account id>).
    public async Task OpenAsync(string? highlight = null)
    {
        await app.GotoAsync(AppRoutes.WithQuery(routes.Members, ("highlight", highlight)));
        await app.WaitForReadyAsync();
        await Expect(MembersHeading).ToBeVisibleAsync();
    }
}
