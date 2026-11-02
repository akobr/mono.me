using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Configurations section's part of the list: the empty state (an h2, "No configuration matches the filter" with a
// filter, "No configurations in this view" without; for Contributors and up its description and a second "New
// configuration" after the navbar's) and the New configuration dialog. With a filter the empty state also shows for a
// moment while the catalog loads.
public sealed partial class ConfigurationsPage
{
    public const string NoMatchTitle = "No configuration matches the filter";

    public const string EmptyStateDescriptionText = "Create one with New, or from the configuration link in the header of an annotation.";

    public ILocator EmptyStateHeading => app.Page.GetByRole(AriaRole.Heading, new() { NameRegex = EmptyStateTitle() });

    public ILocator NoMatchHeading => app.Page.GetByRole(AriaRole.Heading, new() { Name = NoMatchTitle, Exact = true });

    public ILocator EmptyStateDescription => app.Page.GetByText(EmptyStateDescriptionText, new() { Exact = true });

    // Every "New configuration" of the page: the navbar's first (icon only below 768 px), the empty state's last.
    public ILocator NewConfigurationButtons => app.Page.GetByRole(AriaRole.Button, new() { Name = "New configuration", Exact = true });

    public ILocator EmptyStateNewConfigurationButton => NewConfigurationButtons.Last;

    // Opens the list with a filter in its URL (?q=) and waits until the filter box shows it.
    public async Task OpenFilteredAsync(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        await app.GotoAsync(AppRoutes.WithQuery(routes.Configurations, ("q", query)));
        await app.WaitForReadyAsync();
        await Expect(Filter).ToHaveValueAsync(query);
    }

    // Opens New configuration with one of its buttons (NewConfigurationButton, EmptyStateNewConfigurationButton).
    public async Task<NewConfigurationDialog> OpenNewConfigurationAsync(ILocator button)
    {
        ArgumentNullException.ThrowIfNull(button);

        await button.ClickAsync();
        var dialog = new NewConfigurationDialog(app);
        await dialog.ExpectOpenAsync();
        return dialog;
    }

    [GeneratedRegex("^(No configuration matches the filter|No configurations in this view)$")]
    private static partial Regex EmptyStateTitle();
}
