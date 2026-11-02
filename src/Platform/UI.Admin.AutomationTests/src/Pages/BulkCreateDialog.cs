using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// "Bulk create annotations" of the annotations page: one key per line, a live summary ("1 new · 1 already exists · 4
// missing ancestors") and a review row per key (New, Exists, Duplicate or Invalid) against the loaded catalog, then
// "Create N annotation(s)". The result replaces the form: a title ("Created 5 annotations: 1 from the list and 4
// missing ancestors."), a row per created key ("From the list" or "Missing ancestor") and Done. The API creates the
// missing ancestors, so a deep key is enough. No toast.
public sealed partial class BulkCreateDialog(IPage page)
{
    public ILocator Root => page.GetByRole(AriaRole.Dialog, new() { Name = "Bulk create annotations", Exact = true });

    public ILocator Keys => Root.GetByRole(AriaRole.Textbox, new() { Name = "Keys" });

    // The live summary under the keys, empty until the first key is reviewed.
    public ILocator Summary => Root.Locator("p[aria-live=polite]");

    // The title of the result ("Created …" or "No annotations were created.").
    public ILocator Result => Root.GetByText(ResultTitle());

    public ILocator DoneButton => Root.GetByRole(AriaRole.Button, new() { Name = "Done", Exact = true });

    // The table of the created keys (Key, "Created as"); its header row is a row too.
    public ILocator CreatedAnnotations => Root.GetByRole(AriaRole.Region, new() { Name = "Created annotations", Exact = true });

    public ILocator ReviewRow(string key)
    {
        return Root.GetByRole(AriaRole.Region, new() { Name = "Review", Exact = true }).GetByRole(AriaRole.Row).Filter(new() { HasText = key });
    }

    // The row of a created key by its exact key ("From the list" or "Missing ancestor"), not a row of a longer key
    // (sbt.<p>-1 is not sbt.<p>-10).
    public ILocator CreatedRow(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return CreatedAnnotations.GetByRole(AriaRole.Row).Filter(new() { Has = page.GetByText(key, new() { Exact = true }) });
    }

    // Enabled once every line is valid and at least one key is new; count is the number of new keys of the list.
    public ILocator CreateButton(int count)
    {
        var name = count == 1 ? "Create 1 annotation" : string.Create(CultureInfo.InvariantCulture, $"Create {count:N0} annotations");
        return Root.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });
    }

    public async Task EnterKeysAsync(params string[] keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        await Keys.FillAsync(string.Join('\n', keys));
    }

    // Creates the new keys of the list and waits for the result.
    public async Task CreateAsync(int count)
    {
        await CreateButton(count).ClickAsync();
        await Expect(Result).ToBeVisibleAsync();
    }

    public async Task CloseAsync()
    {
        await DoneButton.ClickAsync();
        await Expect(Root).ToBeHiddenAsync();
    }

    // Not "Created as", the header of the created keys.
    [GeneratedRegex(@"^(Created \d|No annotations were created)")]
    private static partial Regex ResultTitle();
}
