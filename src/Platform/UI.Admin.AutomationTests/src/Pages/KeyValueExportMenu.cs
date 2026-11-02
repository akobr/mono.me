using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The values store's Export (components/key-values/KeyValueExportMenu.vue): a dropdown in the navbar (icon-only below
// 640 px, the same name) with the formats "Key/value document (JSON)" ({org}-{project}-{view}.key-values.json),
// ".env file" ({org}-{project}-{view}.env, KEY__PART=value) and "Flat JSON object" ({org}-{project}-{view}.flat.json),
// and, for ContributorWithSecrets and up, the checkbox item "Include secret values", which keeps the menu open and is
// unchecked again after a page load or a project or view switch. A format downloads the whole view at once through a
// Blob link (a Playwright download). Without the secret values the .env and flat formats leave the secrets out with
// the warning toast "<n> secret(s) left out"; with them every format toasts "The file contains secret values".
public sealed partial class KeyValueExportMenu(App app)
{
    public const string Document = "Key/value document (JSON)";
    public const string DotEnv = ".env file";
    public const string FlatJson = "Flat JSON object";

    // The format items in their order.
    public static readonly string[] Formats = [Document, DotEnv, FlatJson];

    public ILocator Button => app.Page.GetByRole(AriaRole.Button, new() { Name = "Export", Exact = true });

    public ILocator Menu => app.Page.GetByRole(AriaRole.Menu);

    // The format items of the open menu (menuitem); the checkbox item has a role of its own.
    public ILocator FormatItems => Menu.GetByRole(AriaRole.Menuitem);

    // aria-checked "true" or "false".
    public ILocator IncludeSecretValues => app.Page.GetByRole(AriaRole.Menuitemcheckbox, new() { Name = "Include secret values", Exact = true });

    // A format item by its exact label (Document, DotEnv, FlatJson).
    public ILocator Format(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return app.Page.GetByRole(AriaRole.Menuitem, new() { Name = label, Exact = true });
    }

    public async Task OpenAsync()
    {
        await Button.ClickAsync();
        await Expect(Menu).ToBeVisibleAsync();
    }

    // Closes the open menu with Escape.
    public async Task CloseAsync()
    {
        await app.Page.Keyboard.PressAsync("Escape");
        await Expect(Menu).ToBeHiddenAsync();
    }

    // Opens the menu and clicks the format: run it inside PlatformTest.DownloadAsync for the file.
    public async Task ExportAsync(string label)
    {
        await OpenAsync();
        await Format(label).ClickAsync();
    }
}
