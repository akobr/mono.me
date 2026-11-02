using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The values store's Import (components/key-values/KeyValueImportDialog.vue): the modal "Import key values" ("Into
// <org>/<project>, view <view>.") in three steps, listed as "1. Source", "2. Review" and "3. Result". It starts empty
// each time it opens.
//   1. Source: a "File" input (its text replaces the textarea) and the textarea "Or paste the text"; "Cancel" and
//      "Next" (disabled while the text is blank). Next parses in the browser: a key/value document, flat or nested
//      JSON, or .env lines ('__' becomes '.', the key lower case); other text shows an error under the textarea.
//   2. Review: the format's badge ("Key/value document", "Flat JSON", "Nested JSON", ".env"), "<n> row(s)", "<n> invalid
//      key(s)", the "Mark keys matching" glob with its level "as" and "Mark" (not for a document, and only with secret
//      rights), a table of Key, Level ("Level of <key>") and Value (a secret's value masked), "Mode" (Merge or Replace,
//      with "Only under prefix"), "Check" (a dry run: "Plan: <counts>" and a line "<Action> <key>" per item), "Back"
//      and "Import" (enabled once there is a plan and no invalid key). An Import of Replace that deletes keys asks
//      "Replace deletes keys" first (type the view's name).
//   3. Result: the alert "Imported" (or "Imported with failures") with the counts, a line "<Action> <key> v<n>" per
//      item, "Undo deletes" after deletes, "Retry failed" after failures, and "Done".
// While the modal is open the rest of the page is aria-hidden, so everything is scoped to it.
public sealed partial class KeyValueImportDialog(App app)
{
    public const string ReplaceMode = "Replace: also delete the other keys";
    public const string MergeMode = "Merge: write the listed keys";

    // The error under the textarea for text of no known format.
    public const string UnreadableText = "The text is neither a key/value document, a JSON object nor .env lines.";

    public ILocator Root => app.Page.GetByRole(AriaRole.Dialog, new() { Name = "Import key values", Exact = true });

    // The list of the three steps; every step's label shows in every step.
    public ILocator Steps => Root.GetByRole(AriaRole.List, new() { Name = "Steps", Exact = true });

    // The current step of the list ("1. Source", "2. Review" or "3. Result"): it has no aria-current, only the classes
    // "font-semibold text-highlighted", so it is found by the class.
    public ILocator CurrentStep => Steps.Locator("li.font-semibold");

    // The native file input of step 1 (accept .json, .env, text/plain, application/json).
    public ILocator FileInput => Root.Locator("input[type=file]");

    // The textarea of step 1, its only textbox.
    public ILocator Text => Root.GetByRole(AriaRole.Textbox, new() { Name = "Or paste the text", Exact = true });

    public ILocator CancelButton => Button("Cancel");

    public ILocator NextButton => Button("Next");

    public ILocator BackButton => Button("Back");

    public ILocator CheckButton => Button("Check");

    // The footer's Import of step 2 (the navbar's Import is outside the dialog).
    public ILocator ImportButton => Button("Import");

    public ILocator DoneButton => Button("Done");

    public ILocator UndoDeletesButton => Button("Undo deletes");

    public ILocator MarkPattern => Root.GetByRole(AriaRole.Textbox, new() { Name = "Mark keys matching", Exact = true });

    // The secret level of Mark: the project's secret levels.
    public ILocator MarkLevel => Root.GetByRole(AriaRole.Combobox, new() { Name = "as", Exact = true });

    public ILocator MarkButton => Button("Mark");

    public ILocator Mode => Root.GetByRole(AriaRole.Combobox, new() { Name = "Mode", Exact = true });

    // Only with Replace: the prefix of the keys Replace may delete; without one it deletes every unlisted key.
    public ILocator Prefix => Root.GetByRole(AriaRole.Textbox, new() { Name = "Only under prefix", Exact = true });

    // "<n> invalid key(s)" of the review, whatever the number.
    public ILocator InvalidCount => Root.GetByText(InvalidCountText());

    // The plan of Check, "Plan: <n> <action>, …" in the order of the response.
    public ILocator Plan => Root.GetByText(PlanText());

    // The alert of step 3, "Imported" (or "Imported with failures"), and its counts.
    public ILocator ResultAlert => Root.GetByText("Imported", new() { Exact = true }).Locator("xpath=ancestor::*[@data-slot='root'][1]");

    public ILocator ResultCounts => ResultAlert.Locator("[data-slot='description']");

    // The confirmation of a Replace that deletes keys (type the view's name; its button "Replace").
    public ConfirmDialog ReplaceDialog => new(app, "Replace deletes keys");

    // A text of the dialog: the format's badge, "<n> row(s)", "<n> invalid key(s)", an error.
    public ILocator Label(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return Root.GetByText(text, new() { Exact = true });
    }

    // The level select of a row of the review, "Level of <key>".
    public ILocator Level(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Root.GetByRole(AriaRole.Combobox, new() { Name = $"Level of {key}", Exact = true });
    }

    // The row of the review's table by its exact key.
    public ILocator Row(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Root.GetByRole(AriaRole.Row).Filter(new() { Has = app.Page.GetByText(key, new() { Exact = true }) });
    }

    // The value of a row of the review ("••••••••" for a secret level).
    public ILocator RowValue(string key)
    {
        return Row(key).GetByRole(AriaRole.Cell).Nth(2);
    }

    // The lines of the plan (step 2) or of the result (step 3) of an action ("Create", "Update", "Unchanged", "Delete").
    public ILocator Lines(string action)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        return Root.GetByRole(AriaRole.Listitem).Filter(new() { Has = app.Page.GetByText(action, new() { Exact = true }) });
    }

    // The line of a key's action in the plan or the result.
    public ILocator Line(string action, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Lines(action).Filter(new() { Has = app.Page.GetByText(key, new() { Exact = true }) });
    }

    // Every line of a key in the plan or the result, whatever its action.
    public ILocator LinesOf(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Root.GetByRole(AriaRole.Listitem).Filter(new() { Has = app.Page.GetByText(key, new() { Exact = true }) });
    }

    // Opens the dialog with the navbar's Import (icon-only below 640 px, the same name) on step 1.
    public async Task OpenAsync()
    {
        await app.Page.GetByRole(AriaRole.Button, new() { Name = "Import", Exact = true }).ClickAsync();
        await Expect(Root).ToBeVisibleAsync();
        await Expect(Text).ToBeVisibleAsync();
    }

    // Pastes the text into step 1 (replacing what is there) and goes on to the review.
    public async Task PasteAndReviewAsync(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        await Text.FillAsync(text);
        await NextAsync();
    }

    // Next of step 1; returns on the review.
    public async Task NextAsync()
    {
        await NextButton.ClickAsync();
        await Expect(CheckButton).ToBeVisibleAsync();
    }

    // Back of step 2; returns on the source with its text.
    public async Task BackAsync()
    {
        await BackButton.ClickAsync();
        await Expect(Text).ToBeVisibleAsync();
    }

    // Marks the rows whose key matches the glob (by default the dialog's "*password*") with a secret level's label.
    public async Task MarkAsync(string level, string? pattern = null)
    {
        if (pattern is not null)
        {
            await MarkPattern.FillAsync(pattern);
        }

        await PickAsync(MarkLevel, level);
        await MarkButton.ClickAsync();
    }

    // Picks the option of a select of the dialog by its exact label and waits until the select shows it.
    public async Task PickAsync(ILocator select, string option)
    {
        ArgumentNullException.ThrowIfNull(select);
        ArgumentException.ThrowIfNullOrWhiteSpace(option);

        await select.ClickAsync();
        await app.Page.GetByRole(AriaRole.Option, new() { Name = option, Exact = true }).ClickAsync();
        await Expect(select).ToHaveTextAsync(option);
    }

    // Check (the dry run); returns once the plan shows.
    public async Task CheckAsync()
    {
        await CheckButton.ClickAsync();
        await Expect(Plan).ToBeVisibleAsync();
    }

    // Import of step 2; returns on the result (a Replace with deletes asks first: confirm ReplaceDialog yourself).
    public async Task ImportAsync()
    {
        await Expect(ImportButton).ToBeEnabledAsync();
        await ImportButton.ClickAsync();
        await Expect(ResultAlert).ToBeVisibleAsync();
    }

    // Done of step 3; returns once the dialog closed.
    public async Task DoneAsync()
    {
        await DoneButton.ClickAsync();
        await Expect(Root).ToBeHiddenAsync();
    }

    // Cancel of step 1; returns once the dialog closed.
    public async Task CancelAsync()
    {
        await CancelButton.ClickAsync();
        await Expect(Root).ToBeHiddenAsync();
    }

    private ILocator Button(string label)
    {
        return Root.GetByRole(AriaRole.Button, new() { Name = label, Exact = true });
    }

    [GeneratedRegex(@"\d+ invalid key\(s\)")]
    private static partial Regex InvalidCountText();

    [GeneratedRegex(@"^\s*Plan: ")]
    private static partial Regex PlanText();
}
