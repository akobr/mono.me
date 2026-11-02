using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// …/configurations/<key>: the configuration of one annotation, with the tab links Edit, Effective, Resolved, Versions
// and Schema. Without an own configuration the Edit tab offers "Create empty" ({} as version 1, the toast
// "Configuration created") and "Start from template". The editor opens in Form mode when a schema applies to the type,
// so a test switches to JSON first. Saving says "Configuration saved (v<n>)". The Effective tab shows the merged
// document (the ancestors, the type's template and the own configuration) read-only. Full screen on a phone.
public sealed partial class ConfigurationDetail(App app, AppRoutes routes)
{
    public ILocator NoConfiguration => app.Page.GetByText("No configuration yet", new() { Exact = true });

    public ILocator CreateEmptyButton => app.Page.GetByRole(AriaRole.Button, new() { Name = "Create empty", Exact = true });

    public ILocator JsonMode => app.Page.GetByRole(AriaRole.Radio, new() { Name = "JSON", Exact = true });

    // CodeMirror's content element, a textbox named by the editor.
    public ILocator Editor => app.Page.GetByRole(AriaRole.Textbox, new() { Name = "Own configuration", Exact = true });

    // Its name ends in the shortcut's keys (CTRL S), hence the regex; not the schema errors' "Save anyway".
    public ILocator SaveButton => app.Page.GetByRole(AriaRole.Button, new() { NameRegex = SaveName() });

    public ILocator EffectiveTab => app.Page.GetByRole(AriaRole.Link, new() { Name = "Effective", Exact = true });

    public ILocator EffectiveDocument => app.Page.GetByRole(AriaRole.Textbox, new() { Name = "Effective configuration", Exact = true });

    // Opens the Edit tab by its URL and waits for the empty state or the editing mode (JSON is there in every mode).
    // On a phone the detail is full screen and hides the navbar, so this does not wait for the app's user menu.
    public async Task OpenAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await app.GotoAsync(routes.Configuration(key));
        await Expect(NoConfiguration.Or(JsonMode)).ToBeVisibleAsync();
    }

    // Creates {} as the own configuration of an annotation without one; the editor replaces the empty state.
    public async Task CreateEmptyAsync()
    {
        await Expect(NoConfiguration).ToBeVisibleAsync();
        await CreateEmptyButton.ClickAsync();
        await Expect(JsonMode).ToBeVisibleAsync();
    }

    // Replaces the own document in the JSON editor. Inserted as one text, not typed: typing would get CodeMirror's
    // closed brackets and completions.
    public async Task SetDocumentAsync(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        await JsonMode.ClickAsync();
        await Editor.ClickAsync();
        await app.Page.Keyboard.PressAsync("ControlOrMeta+A");
        await app.Page.Keyboard.InsertTextAsync(json);
    }

    // Saves the edited document once Save is enabled (the document changed and parses) and waits for the toast that
    // names exactly the expected new version, the only sign of the save: the toast of an earlier save, still on screen,
    // does not count. With the editor's shortcut, not a click: the toast of the step before ("Configuration created")
    // covers the Save button at the bottom right for its 5 seconds, and a pointer on a toast keeps it open. A document
    // its schema rejects gets no toast but the panel "The configuration does not match its schema".
    public async Task SaveAsync(int expectedVersion)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedVersion, 1);

        await Expect(SaveButton).ToBeEnabledAsync();
        await Editor.PressAsync("ControlOrMeta+S");
        await Expect(app.Toast(string.Create(CultureInfo.InvariantCulture, $"Configuration saved (v{expectedVersion})"))).ToBeVisibleAsync();
    }

    public async Task<ILocator> OpenEffectiveAsync()
    {
        await EffectiveTab.ClickAsync();
        await Expect(app.Page).ToHaveURLAsync(EffectivePath());
        return EffectiveDocument;
    }

    [GeneratedRegex("^Save(?! anyway)")]
    private static partial Regex SaveName();

    [GeneratedRegex(@"/effective(?:[?#].*)?$")]
    private static partial Regex EffectivePath();
}
