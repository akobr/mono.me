using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Annotations section's members of "New annotation": the error of a key part, the nested "Deleted annotations" of
// a deleted key, and the choice of an existing name in a parent part.
public sealed partial class NewAnnotationDialog
{
    // The nested dialog of a key or an ancestor that was deleted, asked after Create and before anything is written,
    // with Cancel, "Create new" and "Restore and update".
    public DeletedKeysDialog DeletedKeys => new(app.Page);

    // The error of a key part's field, e.g. "Names cannot contain dots.".
    public ILocator PartError(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return Root.GetByText(message, new() { Exact = true });
    }

    // Picks an existing name of the catalog in a parent part ("Subject", …): typing it lists its own option, and no
    // 'Create "<name>"', which is offered only when no suggestion matches. Closes the list like FillPartAsync.
    public async Task PickExistingPartAsync(string field, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var input = Part(field);
        await input.FillAsync(string.Empty);
        await input.FillAsync(name);

        var existing = app.Page.GetByRole(AriaRole.Option, new() { Name = name, Exact = true });
        await Expect(existing).ToBeVisibleAsync();
        await Expect(app.Page.GetByRole(AriaRole.Option, new() { Name = $"Create \"{name}\"", Exact = true })).ToHaveCountAsync(0);
        await existing.ClickAsync();
        await Expect(input).ToHaveValueAsync(name);
        await Root.GetByRole(AriaRole.Heading, new() { Name = "New annotation", Exact = true }).ClickAsync();
        await Expect(input).ToHaveAttributeAsync("aria-expanded", "false");
    }

    // Clicks Create for a key that was deleted (or below a deleted ancestor) and returns the nested "Deleted
    // annotations" once it shows; nothing is written until a choice.
    public async Task<DeletedKeysDialog> CreateOverDeletedAsync()
    {
        await Expect(CreateButton).ToBeEnabledAsync();
        await CreateButton.ClickAsync();
        var deleted = DeletedKeys;
        await Expect(deleted.Root).ToBeVisibleAsync();
        return deleted;
    }
}
