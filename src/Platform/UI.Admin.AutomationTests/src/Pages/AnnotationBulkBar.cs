using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The bulk bar of the annotations grid (components/annotations/AnnotationBulkBar.vue): the toolbar "Bulk actions" above
// the grid while one or more rows are checked, Contributors and up, on the grid only (from 768 px; the phone list has
// no checkboxes). "<n> selected", then Enable, Disable, Label (a popover with the input "Label", which suggests the
// catalog's labels and offers 'Create "<text>"' for a new one, and Remove and Add), Delete (the confirmation "Delete
// <n> annotations?" with "Delete <n>", no singular form) and "Clear selection". Every successful action shows its toast
// ("Annotations disabled", "Annotations enabled", "Label <l> added", "Label <l> removed", "<n> annotations deleted" with
// Undo for 10 seconds) and clears the selection, which removes the bar.
public sealed partial class AnnotationBulkBar(App app)
{
    public ILocator Root => app.Page.GetByRole(AriaRole.Toolbar, new() { Name = "Bulk actions", Exact = true });

    // "<n> selected".
    public ILocator SelectedCount => Root.GetByText(SelectedText());

    public ILocator EnableButton => Button("Enable");

    public ILocator DisableButton => Button("Disable");

    public ILocator LabelButton => Button("Label");

    public ILocator DeleteButton => Button("Delete");

    public ILocator ClearSelectionButton => Button("Clear selection");

    // The input of the Label popover; it has no accessible name, only its placeholder.
    public ILocator LabelInput => app.Page.GetByPlaceholder("Label", new() { Exact = true });

    // The Label popover around the input.
    public ILocator LabelPopover => app.Page.GetByRole(AriaRole.Dialog).Filter(new() { Has = LabelInput });

    public ILocator AddLabelButton => LabelPopover.GetByRole(AriaRole.Button, new() { Name = "Add", Exact = true });

    public ILocator RemoveLabelButton => LabelPopover.GetByRole(AriaRole.Button, new() { Name = "Remove", Exact = true });

    // The confirmation of Delete for the number of selected rows ("Delete 2 annotations?", also "Delete 1
    // annotations?"), with its button "Delete <n>".
    public ConfirmDialog DeleteDialog(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 1);
        return new ConfirmDialog(app, string.Create(CultureInfo.InvariantCulture, $"Delete {count:N0} annotations?"));
    }

    public ILocator Button(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return Root.GetByRole(AriaRole.Button, new() { Name = label, Exact = true });
    }

    // Opens Label and chooses the label: a new one through 'Create "<label>"', an existing one (a label of the catalog)
    // through its own option, offered instead of Create once a suggestion matches. Returns with Add and Remove enabled
    // and the suggestions closed.
    public async Task ChooseLabelAsync(string label, bool existing)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        await LabelButton.ClickAsync();
        await Expect(LabelInput).ToBeVisibleAsync();
        await LabelInput.FillAsync(label);

        var create = app.Page.GetByRole(AriaRole.Option, new() { Name = $"Create \"{label}\"", Exact = true });
        var option = existing ? app.Page.GetByRole(AriaRole.Option, new() { Name = label, Exact = true }) : create;
        await Expect(option).ToBeVisibleAsync();
        if (existing)
        {
            await Expect(create).ToHaveCountAsync(0);
        }

        await option.ClickAsync();
        await Expect(LabelInput).ToHaveValueAsync(label);

        // The suggestions may stay open after the choice and cover the buttons; a click on the popover's padding, outside
        // the input, closes them (Escape would close the popover).
        if (string.Equals(await LabelInput.GetAttributeAsync("aria-expanded"), "true", StringComparison.Ordinal))
        {
            await LabelPopover.ClickAsync(new() { Position = new() { X = 4, Y = 4 } });
        }

        await Expect(LabelInput).Not.ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(AddLabelButton).ToBeEnabledAsync();
    }

    [GeneratedRegex(@"^[\d,]+ selected$")]
    private static partial Regex SelectedText();
}
