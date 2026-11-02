using System.Globalization;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Modeling section's members of ModelingTemplatePage: the empty state's description, the version badge in the
// editor's actions, the typed delete "Delete the <type> template?" (the confirmation is the type's code), the empty
// state of an unknown type in the URL, and an edit of a value in the document.
public sealed partial class ModelingTemplatePage
{
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

    // Replaces the first occurrence of a text in the template (e.g. "\"retries\":2" by "\"retries\":3"), as one insertion
    // of the whole changed document; the editor then counts the change ("1 change(s)").
    public async Task ReplaceInDocumentAsync(string oldText, string newText)
    {
        ArgumentException.ThrowIfNullOrEmpty(oldText);
        ArgumentNullException.ThrowIfNull(newText);

        var text = await Document.TextAsync();
        var index = text.IndexOf(oldText, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new InvalidOperationException($"The template has no '{oldText}': {text}");
        }

        await Document.ReplaceAsync(string.Concat(text.AsSpan(0, index), newText, text.AsSpan(index + oldText.Length)));
    }

    // Leaves the detail and waits for the list's rows again.
    public async Task CloseToListAsync()
    {
        await Panel.CloseAsync();
        await app.ExpectAtAsync(routes.ModelingTab("templates"));
        await Expect(ListRow("rst")).ToBeVisibleAsync();
    }
}
