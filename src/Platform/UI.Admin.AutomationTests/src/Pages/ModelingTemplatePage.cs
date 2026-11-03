using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// Modeling's Templates tab (…/modeling/templates): a row per type, each one link to …/modeling/templates/<type> named
// by its text (the type, "v<n>" or "No template", the author, the time, "<n> configuration(s)"); and the template
// editor of a type (components/templates/TemplateDetail.vue in the detail section#template-detail, the region "<Type>
// template"): the default configuration of every annotation of the type, merged after the ancestors and before the
// annotation's own content. The tabs "Edit" and "Versions" (?tab=versions, VersionsView). A type without a template
// shows "No <type> template" with "Create template", which puts {} into the editor "Template" and offers "Create"; an
// existing one has "Delete", "<n> change(s)" while changed, "Revert" and "Save". Saving (Ctrl/Cmd+S too) says "Template
// saved (v<n>)"; a concurrent change asks "Someone else changed this configuration". Contributor and up. The type names
// follow the project's settings and the Preferences; a fresh browser shows the built-in ones.
public sealed partial class ModelingTemplatePage(App app, AppRoutes routes)
{
    public DetailPanel Panel { get; } = new(app, "template-detail");

    public CodeEditor Document => new(app, "Template", Panel.Root);

    // The empty state of a type without a template, "No <type> template".
    public ILocator NoTemplate => Panel.Root.GetByRole(AriaRole.Heading, new() { NameRegex = NoTemplateName() });

    public ILocator CreateTemplateButton => Panel.Root.GetByRole(AriaRole.Button, new() { Name = "Create template", Exact = true });

    public ILocator JsonMode => Panel.Root.GetByRole(AriaRole.Radio, new() { Name = "JSON", Exact = true });

    public ILocator EditTab => Panel.Root.GetByRole(AriaRole.Tab, new() { Name = "Edit", Exact = true });

    public ILocator VersionsTab => Panel.Root.GetByRole(AriaRole.Tab, new() { Name = "Versions", Exact = true });

    // The primary button of a template that does not exist yet; Save of an existing one.
    public ILocator CreateButton => Panel.Root.GetByRole(AriaRole.Button, new() { Name = "Create", Exact = true });

    public ILocator SaveButton => Panel.Root.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });

    public ILocator RevertButton => Panel.Root.GetByRole(AriaRole.Button, new() { Name = "Revert", Exact = true });

    // Only for an existing template; it asks "Delete the <type> template?" with the type's code as typed confirmation.
    public ILocator DeleteButton => Panel.Root.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true });

    // "<n> change(s)" of a changed existing template.
    public ILocator Changes => Panel.Root.GetByText(ChangesText());

    public VersionsView Versions => new(app, Panel.Root);

    // The newest toast of a save or a create that failed, with the API's message as its description.
    public ILocator NotSavedToast => app.ToastItem("The template was not saved");

    // Retry of the error state of a template the API refused or did not answer in time.
    private ILocator Retry => ErrorStates.RetryButton(Panel.Root).First;

    // The row of a type in the Templates list, by its code (rst, unt, sbt, usg, cnt, exe, uxe).
    public ILocator ListRow(string type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        return app.Page.GetByRole(AriaRole.Link).And(app.Page.Locator($"a[href$=\"/modeling/templates/{type}\"]"));
    }

    // Opens the Templates list and waits for its rows.
    public async Task OpenListAsync()
    {
        await app.GotoAsync(routes.ModelingTab("templates"));
        await app.WaitForReadyAsync();
        await Expect(ListRow("exe")).ToBeVisibleAsync();
    }

    // Opens the template of a type (its code) and waits until it loaded, up to Target:ApiTimeoutMs: the empty state or
    // the editor; the error state throws with its text. On a phone the detail is full screen and hides the navbar, so
    // this does not wait for the user menu.
    public async Task OpenAsync(string type)
    {
        await app.GotoAsync(routes.Template(type));
        await Expect(NoTemplate.Or(Document.Content).Or(Retry).First).ToBeVisibleAsync(new() { Timeout = app.ApiTimeoutMs });
        if (await Retry.IsVisibleAsync())
        {
            throw new PlaywrightException($"The {type} template did not load: {await ErrorStates.TextAsync(ErrorStates.Around(Retry))}");
        }
    }

    // Replaces the template document as JSON.
    public async Task SetDocumentAsync(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        await JsonMode.ClickAsync();
        await Document.ReplaceAsync(json);
    }

    // Creates the template of a type that has none: "Create template", the document, then Create; waits for the toast
    // "Template saved (v1)".
    public async Task CreateAsync(string json)
    {
        await Expect(NoTemplate).ToBeVisibleAsync();
        await CreateTemplateButton.ClickAsync();
        await SetDocumentAsync(json);
        await SubmitAsync(CreateButton, 1);
    }

    // Saves the changed document of an existing template (a JSON Patch with tests of what it replaces) and waits for the
    // toast of exactly the expected new version.
    public async Task SaveAsync(int expectedVersion)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(expectedVersion, 2);
        await SubmitAsync(SaveButton, expectedVersion);
    }

    // With the editor's shortcut once the button is enabled, not a click: the toast of an earlier save covers the save
    // bar's button at the bottom right for its 5 seconds, and a pointer on a toast keeps it open. The toast is waited for
    // up to Target:ApiWriteTimeoutMs (the admin UI ends the write after its 120 s write timeout); the toast of a failed
    // save (NotSavedToast) throws with its text.
    private async Task SubmitAsync(ILocator button, int expectedVersion)
    {
        await Expect(button).ToBeEnabledAsync();
        await Document.PressSaveAsync();
        var saved = app.Toast(string.Create(CultureInfo.InvariantCulture, $"Template saved (v{expectedVersion})"));
        await Expect(saved.Or(NotSavedToast).First).ToBeVisibleAsync(new() { Timeout = app.ApiWriteTimeoutMs });
        if (!await saved.IsVisibleAsync())
        {
            throw new PlaywrightException($"The template was not saved as v{expectedVersion}: {await ErrorStates.TextAsync(NotSavedToast)}");
        }
    }

    [GeneratedRegex("^No .+ template$")]
    private static partial Regex NoTemplateName();

    [GeneratedRegex(@"^\s*\d+ change\(s\)\s*$")]
    private static partial Regex ChangesText();
}
