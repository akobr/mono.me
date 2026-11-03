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

    // The editor of the own document (JSON or YAML mode), and its CodeMirror content element.
    public CodeEditor Document => new(app, "Own configuration");

    public ILocator Editor => Document.Content;

    // Its name ends in the shortcut's keys (CTRL S), hence the regex; not the schema errors' "Save anyway".
    public ILocator SaveButton => app.Page.GetByRole(AriaRole.Button, new() { NameRegex = SaveName() });

    public ILocator EffectiveTab => app.Page.GetByRole(AriaRole.Link, new() { Name = "Effective", Exact = true });

    // The read-only editor of the Effective tab, and its content element.
    public CodeEditor Effective => new(app, "Effective configuration");

    public ILocator EffectiveDocument => Effective.Content;

    // The newest toast of a save that failed: "The configuration was not saved" with the API's message as its
    // description, or, after a save that did not answer in time and whose content read again is not the one sent,
    // "The API did not answer in time; the change may have been saved. Reload before saving again.".
    public ILocator NotSavedToast => app.ToastItem(NotSavedTitle());

    // The newest toast of "Create empty" or "Start from template" that failed, with the API's message.
    public ILocator NotCreatedToast => app.ToastItem("The configuration was not created");

    // The Retry of the detail's error state: the effective document or the own content did not load. The own content's
    // error shows below the toolbar, whose JSON radio is then visible too.
    private ILocator Retry => ErrorStates.RetryButton(new DetailPanel(app, "configuration-detail").Root).First;

    // Opens the Edit tab by its URL and waits for the empty state or the editing mode (JSON is there in every mode), up
    // to Target:ApiTimeoutMs; the detail's error state throws with its text. On a phone the detail is full screen and
    // hides the navbar, so this does not wait for the app's user menu.
    public async Task OpenAsync(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await app.GotoAsync(routes.Configuration(key));
        await WaitForEditTabAsync();
    }

    // Creates {} as the own configuration of an annotation without one; the editor replaces the empty state.
    public async Task CreateEmptyAsync()
    {
        await Expect(NoConfiguration).ToBeVisibleAsync();
        await CreateEmptyButton.ClickAsync();
        await WaitForCreatedAsync();
    }

    // Replaces the own document in the JSON editor once the own content loaded (up to Target:ApiTimeoutMs; its error
    // state throws). Inserted as one text, not typed: typing would get CodeMirror's closed brackets and completions.
    public async Task SetDocumentAsync(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        await JsonMode.ClickAsync();
        await Expect(Editor.Or(Retry).First).ToBeVisibleAsync(new() { Timeout = app.ApiTimeoutMs });
        await ThrowOnErrorStateAsync("did not load its own content");
        await Document.ReplaceAsync(json);
    }

    // Saves the edited document once Save is enabled (the document changed and parses) and waits for the toast that
    // names exactly the expected new version, the only sign of the save: the toast of an earlier save, still on screen,
    // does not count. With the editor's shortcut, not a click: the toast of the step before ("Configuration created")
    // covers the Save button at the bottom right for its 5 seconds, and a pointer on a toast keeps it open. The toast is
    // waited for up to Target:ApiWriteTimeoutMs: the admin UI ends the patch after its 120 s write timeout and then reads
    // the content again (two reads) before its toast. A save that did not answer in time but was stored says
    // "Configuration saved" without the version; the toast of a failed save (NotSavedToast) throws with its text. A
    // document its schema rejects gets no toast but the panel "The configuration does not match its schema".
    public async Task SaveAsync(int expectedVersion)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedVersion, 1);

        await Expect(SaveButton).ToBeEnabledAsync();
        await Document.PressSaveAsync();
        var saved = app.Toast(string.Create(CultureInfo.InvariantCulture, $"Configuration saved (v{expectedVersion})")).Or(app.Toast("Configuration saved")).First;
        await Expect(saved.Or(NotSavedToast).First).ToBeVisibleAsync(new() { Timeout = app.ApiWriteTimeoutMs });
        if (!await saved.IsVisibleAsync())
        {
            throw new PlaywrightException($"The configuration was not saved as v{expectedVersion}: {await ErrorStates.TextAsync(NotSavedToast)}");
        }
    }

    public async Task<ILocator> OpenEffectiveAsync()
    {
        await EffectiveTab.ClickAsync();
        await Expect(app.Page).ToHaveURLAsync(EffectivePath());
        return EffectiveDocument;
    }

    [GeneratedRegex("^Save(?! anyway)")]
    private static partial Regex SaveName();

    [GeneratedRegex("^(The configuration was not saved|The API did not answer in time; the change may have been saved)")]
    private static partial Regex NotSavedTitle();

    [GeneratedRegex(@"/effective(?:[?#].*)?$")]
    private static partial Regex EffectivePath();

    // The Edit tab after a navigation: the empty state, the editing mode or the detail's error state, which throws.
    private async Task WaitForEditTabAsync()
    {
        await Expect(NoConfiguration.Or(JsonMode).Or(Retry).First).ToBeVisibleAsync(new() { Timeout = app.ApiTimeoutMs });
        await ThrowOnErrorStateAsync("did not load");
    }

    // After "Create empty" or "Start from template": the editor replaces the empty state once the write answered and
    // the documents were read again (the admin UI's write timeout and a read, Target:ApiWriteTimeoutMs). The toast of a
    // failed create or the detail's error state throws with its text.
    private async Task WaitForCreatedAsync()
    {
        await Expect(JsonMode.Or(NotCreatedToast).Or(Retry).First).ToBeVisibleAsync(new() { Timeout = app.ApiWriteTimeoutMs });
        if (await NotCreatedToast.IsVisibleAsync())
        {
            throw new PlaywrightException($"The configuration was not created: {await ErrorStates.TextAsync(NotCreatedToast)}");
        }

        await ThrowOnErrorStateAsync("did not load after it was created");
    }

    private async Task ThrowOnErrorStateAsync(string what)
    {
        if (await Retry.IsVisibleAsync())
        {
            throw new PlaywrightException($"The configuration {what}: {await ErrorStates.TextAsync(ErrorStates.Around(Retry))}");
        }
    }
}
