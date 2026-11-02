using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests;

// The setup of the Graph section's tests: a tree of their own, created on Annotations, because the content of a
// deployed target's view is unknown and keeps growing (the graph tests draw it in focus mode).
public abstract partial class PlatformTest
{
    // Creates a new execution's tree (PlatformData.GraphTree) and the extra keys with Bulk create on Annotations (the
    // command palette on a phone), the first navigation of the test, and returns once the result reads as expected and
    // the dialog closed. result: the dialog's result for the execution and the extra keys.
    protected async Task<GraphTree> CreateGraphTreeAsync(string result = "Created 5 annotations: 1 from the list and 4 missing ancestors.", params string[] extraKeys)
    {
        ArgumentNullException.ThrowIfNull(extraKeys);

        var tree = PlatformData.GraphTree();
        await BulkCreateForGraphAsync(result, [tree.Execution, .. extraKeys]);
        return tree;
    }

    // Bulk create of new keys on Annotations, with their missing ancestors; returns once the result title is the
    // expected one and Done closed the dialog.
    protected async Task BulkCreateForGraphAsync(string result, params string[] keys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(result);
        ArgumentNullException.ThrowIfNull(keys);

        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        var dialog = await annotations.OpenBulkCreateAsync();
        await dialog.EnterKeysAsync(keys);
        await dialog.CreateAsync(keys.Length);
        await Expect(dialog.Result).ToHaveTextAsync(result);
        await dialog.CloseAsync();
    }
}
