using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// "Discard changes?" of an editor with unsaved changes (useUnsavedChanges, a useConfirm modal): the annotation form, a
// configuration, a schema, a template or a key value asks it when the route's path or params change (another tab of
// the detail, another row, Close the detail, Back to the list, a breadcrumb or sidebar link), once per navigation.
// A change of the query alone does not ask. "Keep editing" stays (the URL and the draft unchanged), "Discard" goes on;
// Escape counts as Keep editing. The browser's own prompt of a reload or a closed tab (beforeunload) is not this
// dialog. While an editor is dirty the document title starts with "● ".
public sealed partial class DiscardChangesDialog(App app)
{
    public const string Title = "Discard changes?";

    public const string DescriptionText = "Your changes have not been saved.";

    public ConfirmDialog Dialog { get; } = new(app, Title);

    public ILocator Root => Dialog.Root;

    public ILocator Description => Dialog.Description;

    public ILocator KeepEditingButton => Dialog.Button("Keep editing");

    public ILocator DiscardButton => Dialog.Button("Discard");

    public async Task ExpectOpenAsync()
    {
        await Dialog.ExpectOpenAsync();
    }

    // Stays on the page with the draft; the navigation does not happen.
    public async Task KeepEditingAsync()
    {
        await Dialog.CancelAsync("Keep editing");
    }

    // Drops the draft; the navigation goes on.
    public async Task DiscardAsync()
    {
        await Dialog.ConfirmAsync("Discard");
    }
}
