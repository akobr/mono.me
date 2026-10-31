using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation.Runtime;
using _42.Testing.UI.Automation.Runtime.Users;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests;

// The Platform's target check, once per run before the first test (PlatformAutomationSetup.CheckTargetAsync), on a page
// signed in as a pool user: the annotations of Target:Organization / Project / View open with the catalog loaded, and a
// view other than the default is one of the project's views. A good target costs one page load (two for another view).
// A bad one fails with what to fix: the organization not found, the project not found, the view not found, or no
// access. The admin UI shows an organization or a project outside the user's memberships as "no access" whether it
// exists or not, so the check reads the user's memberships on /orgs to tell them apart; "not found" there also means
// that the user is not a member.
internal sealed class PlatformTargetCheck
{
    private readonly PlatformTarget target;
    private readonly TestUser user;
    private readonly App app;
    private readonly AppRoutes routes;

    public PlatformTargetCheck(IPage page, PlatformTarget target, TestUser user)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(user);

        this.target = target;
        this.user = user;
        app = new App(page, target.MockScenario);
        routes = AppRoutes.For(target);
    }

    private string Project => $"{target.Organization}/{target.Project}";

    private string View => $"{Project}/{target.View}";

    public async Task RunAsync()
    {
        var annotations = new AnnotationsPage(app, routes);
        await app.GotoAsync(routes.Annotations);
        await ExpectShownAsync(annotations.CatalogLoaded.Or(annotations.CatalogError).Or(app.NoAccessHeading).First, $"The annotations of {View}");

        if (await app.NoAccessHeading.IsVisibleAsync())
        {
            throw await NoAccessAsync();
        }

        if (await annotations.CatalogError.IsVisibleAsync())
        {
            throw new TargetCheckException($"The annotations of {View} did not load for {user}: {await ErrorTextAsync(annotations.CatalogError)}");
        }

        if (!string.Equals(target.View, AppRoutes.DefaultView, StringComparison.Ordinal))
        {
            await CheckViewAsync();
        }
    }

    private static string List(IEnumerable<string> names)
    {
        var listed = string.Join(", ", names);
        return listed.Length == 0 ? "none" : listed;
    }

    // The text of an error state: its heading, the API's message and hint, without the Retry button's label.
    private static async Task<string> ErrorTextAsync(ILocator heading)
    {
        var text = await heading.Locator("xpath=..").InnerTextAsync();
        return string.Join(" ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(line => line != "Retry"));
    }

    private async Task ExpectShownAsync(ILocator shown, string what)
    {
        try
        {
            await Expect(shown).ToBeVisibleAsync();
        }
        catch (PlaywrightException exception)
        {
            throw new TargetCheckException($"{what} did not show for {user}; the page is at {app.Page.Url}: {exception.Message}", exception);
        }
    }

    // The annotations show "You have no access here": the organization or the project is outside the user's
    // memberships, or the user's role in the project is None.
    private async Task<TargetCheckException> NoAccessAsync()
    {
        var organizations = new OrganizationsPage(app, routes);
        await organizations.OpenAsync();
        await ExpectShownAsync(organizations.Heading, "The organizations");
        var listed = await organizations.ListedAsync();

        if (!listed.TryGetValue(target.Organization, out var projects))
        {
            return new TargetCheckException(
                $"Target:Organization '{target.Organization}' was not found: it is not among the organizations of {user} ({List(listed.Keys)}). "
                + "The organization does not exist, or the user is not a member of it or of one of its projects; fix Target:Organization or the user's memberships.");
        }

        if (!projects.Contains(target.Project, StringComparer.Ordinal))
        {
            return new TargetCheckException(
                $"Target:Project '{target.Project}' was not found in organization '{target.Organization}': it is not among the projects of {user} there ({List(projects)}). "
                + "The project does not exist, or the user has no role in it (a role in the organization does not grant one); fix Target:Project or the user's project role.");
        }

        return new TargetCheckException(
            $"{user} has no access to {Project}: the project is listed for the user, but its annotations show \"You have no access here\". "
            + $"Give the user a role in the project (Users:Pool lists {List(user.Roles)} for it).");
    }

    private async Task CheckViewAsync()
    {
        var views = new ViewsPage(app, routes);
        await views.OpenAsync();

        if (await views.LoadError.IsVisibleAsync())
        {
            throw new TargetCheckException($"The views of {Project} did not load for {user}; the page is at {app.Page.Url}.");
        }

        if (await views.View(target.View).CountAsync() == 0)
        {
            throw new TargetCheckException(
                $"Target:View '{target.View}' was not found in {Project}: the project's views are {List(await views.ListedAsync())}. "
                + "Create it on the project's Views page (New view, an Administrator; a view only the data holds is listed and registered there by an Administrator) or fix Target:View.");
        }
    }
}
