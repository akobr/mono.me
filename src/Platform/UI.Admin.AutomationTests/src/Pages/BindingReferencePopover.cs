using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The binding reference (components/configuration/BindingReference.vue): a popover, teleported to the body, with the
// region "Binding reference". It opens from the toolbar's "fx Bindings" of the Edit and Effective tabs, or from the
// "Binding reference" button of a binding field in the form, the only place with "Use" (it puts an example into the
// field and closes the popover). The textbox "Filter the binding reference" narrows the sections "Expressions",
// "Functions", "Sources" and "Object bindings" to their matching entries, article[data-binding="<id>"]
// (function-config, function-annotation, source-<name>, …), and says "Nothing matches “<filter>”." when nothing does.
// A deployed target lists the functions and sources its server registers; config is one on every target.
public sealed partial class BindingReferencePopover(App app)
{
    public ILocator Region => app.Page.GetByRole(AriaRole.Region, new() { Name = "Binding reference", Exact = true });

    public ILocator Filter => Region.GetByRole(AriaRole.Textbox, new() { Name = "Filter the binding reference", Exact = true });

    // Every "Use" of the reference; none when it is not insertable (the toolbar's).
    public ILocator UseButtons => Region.GetByRole(AriaRole.Button, new() { Name = "Use", Exact = true });

    // The heading of a section: "Expressions", "Functions", "Sources" or "Object bindings".
    public ILocator SectionHeading(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return Region.GetByRole(AriaRole.Heading, new() { Name = title, Exact = true });
    }

    // An entry by its id, e.g. "function-config"; its examples follow its summary and syntax. A data attribute: the
    // entries have no name of their own.
    public ILocator Entry(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Region.Locator($"article[data-binding=\"{id}\"]");
    }

    // The text of a filter that matches nothing.
    public ILocator NothingMatches(string filter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filter);
        return Region.GetByText($"Nothing matches “{filter}”.", new() { Exact = true });
    }

    public async Task FilterAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        await Filter.FillAsync(text);
    }
}
