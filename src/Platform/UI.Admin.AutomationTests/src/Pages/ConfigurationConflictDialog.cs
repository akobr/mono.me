using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// "Someone else changed this configuration" (components/configuration/ConflictDialog.vue): a save whose JSON Patch test
// failed (412 PatchTestFailed) because another writer changed a value the save replaces or removes. It reloads theirs
// (the stored own content) and compares it with yours in a DiffEditor, side by side with the labels "Theirs (stored)"
// and "Yours" from 1024 px, unified without labels below. "Copy mine" copies your JSON (the label reads "Copied" for
// 1.5 s), "Reload, discard mine" takes theirs into the editor, "Overwrite" (disabled while theirs loads) saves yours
// over theirs. Every choice closes the dialog.
public sealed partial class ConfigurationConflictDialog(App app)
{
    public const string Title = "Someone else changed this configuration";

    public const string DescriptionText = "The stored configuration changed after you opened it. Compare their version (left) with yours (right).";

    public ILocator Root => app.Page.GetByRole(AriaRole.Dialog, new() { Name = Title, Exact = true });

    public ILocator Description => Root.GetByText(DescriptionText, new() { Exact = true });

    // The side labels of the split layout (from 1024 px).
    public ILocator TheirsLabel => Root.GetByText("Theirs (stored)", new() { Exact = true });

    public ILocator YoursLabel => Root.GetByText("Yours", new() { Exact = true });

    public ILocator CopyMineButton => Root.GetByRole(AriaRole.Button, new() { Name = "Copy mine", Exact = true });

    public ILocator CopiedButton => Root.GetByRole(AriaRole.Button, new() { Name = "Copied", Exact = true });

    public ILocator ReloadButton => Root.GetByRole(AriaRole.Button, new() { Name = "Reload, discard mine", Exact = true });

    public ILocator OverwriteButton => Root.GetByRole(AriaRole.Button, new() { Name = "Overwrite", Exact = true });

    // Waits until the dialog shows with theirs loaded (Overwrite is enabled then).
    public async Task ExpectOpenAsync()
    {
        await Expect(Root).ToBeVisibleAsync();
        await Expect(OverwriteButton).ToBeEnabledAsync();
    }

    // Saves yours over theirs; the dialog closes and the page saves (its toast follows).
    public async Task OverwriteAsync()
    {
        await Expect(OverwriteButton).ToBeEnabledAsync();
        await OverwriteButton.ClickAsync();
        await Expect(Root).ToBeHiddenAsync();
    }

    // Takes theirs into the editor; the dialog closes.
    public async Task ReloadAsync()
    {
        await ReloadButton.ClickAsync();
        await Expect(Root).ToBeHiddenAsync();
    }
}
