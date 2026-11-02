using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// /<organization>/<project>/views: the project's views, each a link named by the view to its annotations. The default
// view is always listed; a view that only the project's data holds (not registered) only for an Administrator, as
// "discovered". While the list loads the page shows a spinner, and an error state with "Retry" when the API refuses.
// An Administrator creates a view with "New view" (the dialog "New view": "Name" (required, a name like a project's),
// "Description", "Create view"); the toast "View <name> created" follows and the list shows it. A name that exists is
// the field error "A view with this name already exists.".
public sealed partial class ViewsPage(App app, AppRoutes routes)
{
    // "Retry" of the error state.
    public ILocator LoadError => app.Page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true });

    // In the navbar, for Administrators and up.
    public ILocator NewViewButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "New view", Exact = true });

    public ILocator NewViewDialog => app.Page.GetByRole(AriaRole.Dialog, new() { Name = "New view", Exact = true });

    // A required field's accessible name ends in '*' ("Name*").
    public ILocator NewViewName => NewViewDialog.GetByRole(AriaRole.Textbox, new() { NameRegex = NameLabel() });

    public ILocator NewViewDescription => NewViewDialog.GetByRole(AriaRole.Textbox, new() { Name = "Description", Exact = true });

    public ILocator CreateViewButton => NewViewDialog.GetByRole(AriaRole.Button, new() { Name = "Create view", Exact = true });

    public ILocator View(string view)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(view);
        return app.Page.GetByRole(AriaRole.Link, new() { Name = view, Exact = true })
            .And(app.Page.Locator($"a[href$=\"/{routes.Organization}/{routes.Project}/{view}/annotations\"]"));
    }

    // Opens the page; returns once the list (with the default view) or the error state shows.
    public async Task OpenAsync()
    {
        await app.GotoAsync(routes.Views);
        await app.WaitForReadyAsync();
        await Expect(View(AppRoutes.DefaultView).Or(LoadError).Or(app.NoAccessHeading).First).ToBeVisibleAsync();
    }

    // Creates a view of the project with "New view" and returns once the toast "View <name> created" showed, the dialog
    // closed and the list has the view. Returns the routes of the new view.
    public async Task<AppRoutes> CreateViewAsync(string name, string? description = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await NewViewButton.ClickAsync();
        await Expect(NewViewDialog).ToBeVisibleAsync();
        await NewViewName.FillAsync(name);
        if (description is not null)
        {
            await NewViewDescription.FillAsync(description);
        }

        await CreateViewButton.ClickAsync();
        await Expect(app.Toast($"View {name} created")).ToBeVisibleAsync();
        await Expect(NewViewDialog).ToBeHiddenAsync();
        await Expect(View(name)).ToBeVisibleAsync();
        return routes with { View = name };
    }

    // The names of the listed views, in order.
    public async Task<IReadOnlyList<string>> ListedAsync()
    {
        var pattern = new Regex($@"/{Regex.Escape(routes.Organization)}/{Regex.Escape(routes.Project)}/(?<view>[^/?#]+)/annotations/?(?:[?#].*)?$");
        return (await app.ContentLinksAsync())
            .Select(href => pattern.Match(href))
            .Where(match => match.Success)
            .Select(match => Uri.UnescapeDataString(match.Groups["view"].Value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    [GeneratedRegex(@"^Name\*?$")]
    private static partial Regex NameLabel();
}
