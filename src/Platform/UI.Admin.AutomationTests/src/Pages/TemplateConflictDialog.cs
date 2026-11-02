using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// "Someone else changed this configuration" (components/configuration/ConflictDialog.vue) of the template editor: its
// save was a JSON Patch whose test of a value failed (412 PatchTestFailed), because the stored template changed after
// it was read. The dialog (the same text as for a configuration) compares the stored document ("Theirs (stored)") with
// the draft ("Yours") in a diff, side by side from lg (1024 px) and unified below, where the labels show only after
// "Split" of "Diff layout". "Copy mine" copies the draft as indented JSON and reads "Copied" for a moment; "Reload,
// discard mine" takes the stored document as the draft; "Overwrite" saves the draft over the stored one.
public sealed partial class TemplateConflictDialog(App app)
{
    public const string Title = "Someone else changed this configuration";

    // From this width the diff is side by side with its labels.
    public const int SplitDiffWidth = 1024;

    public ILocator Root => app.Page.GetByRole(AriaRole.Dialog, new() { Name = Title, Exact = true });

    public ILocator TheirsLabel => Root.GetByText("Theirs (stored)", new() { Exact = true });

    public ILocator YoursLabel => Root.GetByText("Yours", new() { Exact = true });

    public ILocator SplitLayoutTab => Root.GetByRole(AriaRole.Tab, new() { Name = "Split", Exact = true });

    public ILocator CopyMineButton => Root.GetByRole(AriaRole.Button, new() { Name = "Copy mine", Exact = true });

    // "Copy mine" right after a copy.
    public ILocator CopiedButton => Root.GetByRole(AriaRole.Button, new() { Name = "Copied", Exact = true });

    public ILocator ReloadButton => Root.GetByRole(AriaRole.Button, new() { Name = "Reload, discard mine", Exact = true });

    public ILocator OverwriteButton => Root.GetByRole(AriaRole.Button, new() { Name = "Overwrite", Exact = true });

    public async Task ExpectOpenAsync()
    {
        await Expect(Root).ToBeVisibleAsync();
    }

    // Shows the diff side by side with its labels: "Split" below lg, where "Auto" is unified; nothing from lg.
    public async Task ShowSplitDiffAsync()
    {
        if (app.Page.ViewportSize is { Width: < SplitDiffWidth })
        {
            await SplitLayoutTab.ClickAsync();
            await Expect(SplitLayoutTab).ToHaveAttributeAsync("aria-selected", "true");
        }

        await Expect(TheirsLabel).ToBeVisibleAsync();
    }

    // Takes the stored template as the draft and waits until the dialog closed.
    public async Task ReloadAsync()
    {
        await ReloadButton.ClickAsync();
        await Expect(Root).ToBeHiddenAsync();
    }

    // Saves the draft over the stored template once the stored one loaded; the dialog closes before the save, whose
    // toast the caller asserts.
    public async Task OverwriteAsync()
    {
        await Expect(OverwriteButton).ToBeEnabledAsync();
        await OverwriteButton.ClickAsync();
        await Expect(Root).ToBeHiddenAsync();
    }
}
