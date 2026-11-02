using System.Globalization;
using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Modeling section's members of ModelingTemplatePage: the rows of the list, the empty state's description, the
// version badge in the editor's actions, the typed delete "Delete the <type> template?" (the confirmation is the type's
// code), the empty states of an unknown type in the URL and of the Versions of a missing template.
public sealed partial class ModelingTemplatePage
{
    // The rows of the list, one link per type: the list's items (the list that holds the Responsibility row).
    public ILocator TemplateListRows => app.Page.GetByRole(AriaRole.List)
        .Filter(new() { Has = app.Page.Locator("a[href$=\"/modeling/templates/rst\"]") })
        .GetByRole(AriaRole.Listitem);

    public ILocator NoTemplateDescription => Panel.Root.GetByText("Configurations of this type inherit only from their ancestors.", new() { Exact = true });

    // The detail of a type code the app does not know (…/modeling/templates/foo).
    public ILocator UnknownType => Panel.Root.GetByRole(AriaRole.Heading, new() { Name = "Unknown annotation type", Exact = true });

    // The Versions tab of a type without a template.
    public ILocator NoHistory => Panel.Root.GetByRole(AriaRole.Heading, new() { Name = "No template, no history", Exact = true });

    // The badge "v<n>" of the stored template, beside the editor's format switch.
    public ILocator VersionBadge(int version)
    {
        return Panel.Root.GetByText(string.Create(CultureInfo.InvariantCulture, $"v{version}"), new() { Exact = true });
    }

    // The typed delete of the open template, by the type's label in lower case ("responsibility"): "Delete the
    // responsibility template?", with the field "Type <code> to confirm".
    public ConfirmDialog DeleteDialog(string typeLabel)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeLabel);
        return new ConfirmDialog(app, $"Delete the {typeLabel} template?");
    }
}
