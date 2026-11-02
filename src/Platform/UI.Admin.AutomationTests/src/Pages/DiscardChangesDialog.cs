using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// "Discard changes?" of an editor with unsaved changes (useUnsavedChanges, a useConfirm modal): the annotation form, a
// configuration, a schema, a template or a key value asks it when the route's path or params change (another tab of
// the detail, another row, Close the detail, Back to the list, a breadcrumb or sidebar link), once per navigation.
// A change of the query alone does not ask. "Keep editing" stays (the URL and the draft unchanged), "Discard" goes on;
// Escape counts as Keep editing. The browser's own prompt of a reload or a closed tab (beforeunload) is not this
// dialog. While an editor is dirty the document title starts with "● " (DirtyTitle).
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

    // The document title while an editor is dirty: it starts with "● ".
    [GeneratedRegex("^● ")]
    public static partial Regex DirtyTitle();

    // Drops the draft; the navigation goes on.
    public async Task DiscardAsync()
    {
        await Dialog.ConfirmAsync("Discard");
    }

    // After a click that navigates and may ask (e.g. leaving a detail whose editor is dirty with a known bug of the
    // admin UI and may be clean without it): waits until either this dialog or the outcome of the navigation shows, and
    // discards the draft only if the dialog asked, so the navigation goes on. The outcome must be something that exists
    // only after the navigation (the next tab's content, a detail gone: DetailPanel.Gone), never something already
    // shown before the click; while the modal is open the page is aria-hidden, so a role locator of the page cannot
    // match then.
    public async Task DiscardIfShownAsync(ILocator outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        await Expect(Root.Or(outcome).First).ToBeVisibleAsync();
        if (await Root.IsVisibleAsync())
        {
            await DiscardAsync();
        }
    }
}
