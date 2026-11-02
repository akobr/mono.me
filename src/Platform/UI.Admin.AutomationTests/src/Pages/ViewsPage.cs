using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// /<organization>/<project>/views: the project's views, each a link named by the view to its annotations. The default
// view is always listed; a view that only the project's data holds (not registered) only for an Administrator, as
// "discovered". While the list loads the page shows a spinner, and an error state with "Retry" when the API refuses.
public sealed partial class ViewsPage(App app, AppRoutes routes)
{
    // "Retry" of the error state.
    public ILocator LoadError => app.Page.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true });

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
}
