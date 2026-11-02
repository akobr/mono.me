using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The card "Key-encryption keys" of the Secrets section (settings#secrets; components/secrets/SecretsKeysCard.vue): a
// row for the Standard key, then one for the HSM key, each with the level's badge (the HSM row also with its backend,
// "Key Vault Premium" or "Managed HSM"). A row with a key shows "Key" (the last segment of its id, the full id as the
// title), "Current version" and "Next rotation", and for Administrators "Rotate now" (both rows' buttons have that
// name, so a row is picked by its badge, never by index); a row without one says when it comes ("Created with the
// first secret, or by the daily check." for Standard, which a mock project has at once, "Created when the HSM level is
// allowed." for HSM). "Rotate now" asks "Rotate the <Standard|HSM> key now?"; "Rotate" shows the toast "<Standard|HSM>
// key rotated" and the re-wrap alert under the keys: "Re-wrap of the <level> values complete" (the mock, at once) or
// "Re-wrapping the <level> values" (a deployed API, until the hourly maintenance), with the API's level name (Standard,
// Hsm) and a description that ends with "target key version <version>.".
public sealed partial class KeyEncryptionKeysCard(App app)
{
    public const string RotateDialogDescription = "New values use the new key version at once; the stored values are re-wrapped in the background. The old version stays usable until the re-wrap is complete.";

    public const string NoHsmKey = "Created when the HSM level is allowed.";

    // The labels of the rows' badges, by the level's label or its API name.
    private static readonly Dictionary<string, string> RowLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Standard"] = "Standard",
        ["Hsm"] = "HSM",
    };

    public ILocator Root => app.Page
        .GetByRole(AriaRole.Heading, new() { Name = "Key-encryption keys", Exact = true, Level = 3 })
        .Locator("xpath=ancestor::*[@data-slot='root'][1]");

    // The title of the re-wrap alert under the keys (Nuxt UI's alert has no role).
    public ILocator RewrapTitle => Root.Locator("[data-slot='title']");

    // The progress of the re-wrap: "<processed> processed, <remaining> remaining; target key version <version>."
    public ILocator RewrapDescription => Root.Locator("[data-slot='description']");

    // The row of a level's key, "Standard" or "HSM" (or the API name Hsm): the element that holds this level's badge and
    // not the other one's, the outermost first.
    public ILocator Row(string level)
    {
        var label = Label(level);
        var other = label == "HSM" ? "Standard" : "HSM";
        return Root.Locator("div")
            .Filter(new() { Has = Badge(label), HasNot = Badge(other) })
            .First;
    }

    // The value of a term of the row's key: "Key", "Current version" or "Next rotation".
    public ILocator KeyValue(string level, string term)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(term);
        return Row(level).Locator($"dt:text-is(\"{term}\") + dd");
    }

    public ILocator CurrentVersion(string level)
    {
        return KeyValue(level, "Current version");
    }

    public ILocator RotateButton(string level)
    {
        return Row(level).GetByRole(AriaRole.Button, new() { Name = "Rotate now", Exact = true });
    }

    // A text of the row, e.g. NoHsmKey or the HSM backend ("Key Vault Premium", "Managed HSM").
    public ILocator RowText(string level, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return Row(level).GetByText(text, new() { Exact = true });
    }

    public ILocator RowText(string level, Regex text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Row(level).GetByText(text);
    }

    public ConfirmDialog RotateDialog(string level)
    {
        return new ConfirmDialog(app, $"Rotate the {Label(level)} key now?");
    }

    // Reads the current version of the row's key once it shows one.
    public async Task<string> CurrentVersionAsync(string level)
    {
        await Expect(CurrentVersion(level)).ToHaveTextAsync(Version());
        return (await CurrentVersion(level).InnerTextAsync()).Trim();
    }

    private static string Label(string level)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(level);
        return RowLabels.TryGetValue(level, out var label)
            ? label
            : throw new ArgumentException($"'{level}' has no key of the platform; use Standard or HSM.", nameof(level));
    }

    // The level's badge: its label, exactly.
    private ILocator Badge(string label)
    {
        return app.Page.GetByText(label, new() { Exact = true });
    }

    // A key version: hex digits, or another non-blank text of a deployed Key Vault.
    [GeneratedRegex(@"^\S+$")]
    private static partial Regex Version();
}
