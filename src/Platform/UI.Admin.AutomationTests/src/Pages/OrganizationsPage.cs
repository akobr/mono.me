using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// /orgs: one card per organization of the user, with the organization's link and the links of its projects, whose
// accessible names end in the user's role ("billing Owner").
public sealed partial class OrganizationsPage(App app, AppRoutes routes)
{
    // The top-level pages of the app, which are not organizations.
    private static readonly HashSet<string> AppPages = new(["account", "callback", "invitations", "login", "onboarding", "orgs", "unsupported"], StringComparer.Ordinal);

    public ILocator Heading => app.Page.GetByRole(AriaRole.Heading, new() { Name = "Organizations", Exact = true, Level = 1 });

    // Labelled on wide screens, icon-only with the same accessible name on a phone.
    public ILocator NewOrganization => app.Page.GetByRole(AriaRole.Button, new() { Name = "New organization", Exact = true });

    public ILocator Organization(string organization)
    {
        return app.Page.GetByRole(AriaRole.Link, new() { Name = organization, Exact = true });
    }

    // The project's link in its organization's card: named "<project> <role>", pointing to /<organization>/<project>/….
    public ILocator Project(string organization, string project)
    {
        return app.Page
            .GetByRole(AriaRole.Link, new() { NameRegex = new Regex($@"^{Regex.Escape(project)}\b") })
            .And(app.Page.Locator($"a[href*=\"/{organization}/{project}/\"]"));
    }

    public async Task OpenAsync()
    {
        await app.GotoAsync(routes.Organizations);
        await app.WaitForReadyAsync();
    }

    // The organizations the page lists, each with its listed projects (a project's link is
    // /<organization>/<project>/default/annotations), by name; for a message that says what the user can see.
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> ListedAsync()
    {
        var listed = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var href in await app.ContentLinksAsync())
        {
            var path = href.Split('?', '#')[0];
            if (ProjectLink().Match(path) is { Success: true } project)
            {
                Of(listed, project.Groups["org"].Value).Add(project.Groups["project"].Value);
            }
            else if (OrganizationLink().Match(path) is { Success: true } organization && !AppPages.Contains(organization.Groups["org"].Value))
            {
                Of(listed, organization.Groups["org"].Value);
            }
        }

        return listed.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<string>)[.. entry.Value], StringComparer.Ordinal);
    }

    private static SortedSet<string> Of(SortedDictionary<string, SortedSet<string>> listed, string organization)
    {
        if (!listed.TryGetValue(organization, out var projects))
        {
            listed[organization] = projects = new SortedSet<string>(StringComparer.Ordinal);
        }

        return projects;
    }

    [GeneratedRegex(@"/(?<org>[a-z0-9][a-z0-9-]*)/(?<project>[a-z0-9][a-z0-9-]*)/default/annotations/?$")]
    private static partial Regex ProjectLink();

    [GeneratedRegex(@"^/(?<org>[a-z0-9][a-z0-9-]*)/?$")]
    private static partial Regex OrganizationLink();
}
