using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// "Deleted annotations" (components/annotations/DeletedKeysDialog.vue): a second modal on top of "New annotation" or
// "Bulk create annotations" when a create would touch deleted keys or ancestors, asked after the deleted-key lookup
// (POST …/annotations/deleted/lookup) and before anything is written. Its list "Deleted annotations" has a row per
// deleted key: the type, the key, its title and "Deleted <relative time> by <actor>" (with "with <n> descendant(s)" for
// the root of a deletion). The footer: "Cancel" (as closing it: nothing is written, the create dialog stays with its
// input), "Create new" (the API re-creates the keys empty, a new lifetime) and the focused restore button, "Restore and
// update" in New annotation (the key comes back with its content, the dialog's fields written over it) or "Restore" in
// Bulk create (the keys come back alone and are not created again).
public sealed partial class DeletedKeysDialog(IPage page)
{
    public const string RestoreAndUpdateLabel = "Restore and update";

    public const string RestoreLabel = "Restore";

    public ILocator Root => page.GetByRole(AriaRole.Dialog, new() { Name = "Deleted annotations", Exact = true });

    public ILocator Items => Root.GetByRole(AriaRole.List, new() { Name = "Deleted annotations", Exact = true }).GetByRole(AriaRole.Listitem);

    public ILocator CancelButton => Button("Cancel");

    public ILocator CreateNewButton => Button("Create new");

    // "Restore and update" (New annotation) or "Restore" (Bulk create).
    public ILocator Button(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return Root.GetByRole(AriaRole.Button, new() { Name = label, Exact = true });
    }

    // The row of a deleted key by its exact key: its title and "Deleted … by <actor>".
    public ILocator Item(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Items.Filter(new() { Has = page.GetByText(key, new() { Exact = true }) });
    }

    // Closes it without a choice: the create stops and writes nothing.
    public async Task CancelAsync()
    {
        await CancelButton.ClickAsync();
        await Expect(Root).ToBeHiddenAsync();
    }

    // The API re-creates the deleted keys empty.
    public async Task CreateNewAsync()
    {
        await CreateNewButton.ClickAsync();
        await Expect(Root).ToBeHiddenAsync();
    }

    // Restores the deleted keys: label is RestoreAndUpdateLabel in New annotation, RestoreLabel in Bulk create.
    public async Task RestoreAsync(string label)
    {
        await Button(label).ClickAsync();
        await Expect(Root).ToBeHiddenAsync();
    }
}
