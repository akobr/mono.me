using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The members of the configurations page that the Navigation section uses.
public sealed partial class ConfigurationsPage
{
    // The heading of the empty state of a view without configurations (a new or never registered view), without a
    // filter; "No configuration matches the filter" with one.
    public ILocator EmptyViewHeading => app.Page.GetByRole(AriaRole.Heading, new() { Name = "No configurations in this view", Exact = true });

    // The loaded configurations: the table from md (768 px), the list's items below it, or the empty state of a view
    // without configurations ("Loading configurations…" is gone then).
    public ILocator LoadedConfigurations => EmptyViewHeading
        .Or(HasGrid
            ? app.Page.GetByRole(AriaRole.Table)
            : app.Page.GetByRole(AriaRole.Listitem).Filter(new() { Has = app.Page.GetByRole(AriaRole.Button) }))
        .First;
}
