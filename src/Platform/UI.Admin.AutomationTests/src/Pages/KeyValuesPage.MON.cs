using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// Monitoring's members of the values store: the Export menu of the navbar (KeyValueExportMenu.vue), whose export with
// secret values is an audit event. "Export" (its label is hidden below sm, its name stays) opens a menu with the
// formats "Key/value document (JSON)", ".env file" and "Flat JSON object" and, for the secrets capability, the
// checkbox item "Include secret values" (aria-checked; the menu stays open when it is toggled). A format downloads
// <org>-<project>-<view>.<format> at once; with secret values the toast "The file contains secret values" follows.
public sealed partial class KeyValuesPage
{
    public ILocator ExportMenuTrigger => app.Page.GetByRole(AriaRole.Button, new() { Name = "Export", Exact = true });

    public ILocator ExportSecretValuesCheckbox => app.Page.GetByRole(AriaRole.Menuitemcheckbox, new() { Name = "Include secret values", Exact = true });

    // A format of the open Export menu by its exact label, e.g. "Flat JSON object".
    public ILocator ExportFormatMenuItem(string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);
        return app.Page.GetByRole(AriaRole.Menuitem, new() { Name = format, Exact = true });
    }
}
