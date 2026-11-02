using System.Globalization;
using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Configurations section's part of the version history: the side labels of a comparison and a pick in one
// picker. A comparison's DiffEditor is side by side from 1024 px, with "v<from>" and "v<to>" over its sides; below it
// is the unified view without labels. The same "v<n>" is also the header's, the timeline's and a picker's text, so a
// label is found inside the diff view: the innermost element with both the switch "Collapse unchanged" and the
// editors.
public sealed partial class VersionsView
{
    // The DiffEditor of a comparison (its toolbar, the side labels and the editors).
    public ILocator CompareDiffView => Scope.Locator("div")
        .Filter(new() { Has = CollapseUnchanged })
        .Filter(new() { Has = app.Page.GetByRole(AriaRole.Textbox) })
        .Last;

    // The label of a side of the split comparison, "v<n>".
    public ILocator CompareSideLabel(int version)
    {
        return CompareDiffView.GetByText(Label(version), new() { Exact = true });
    }

    // Picks a version in one picker (FromSelect or ToSelect) without comparing yet.
    public async Task PickVersionAsync(ILocator select, int version)
    {
        ArgumentNullException.ThrowIfNull(select);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);

        await PickAsync(select, version);
    }

    // "Version v<n>", the title of one shown version.
    public ILocator ShownVersionTitle(int version)
    {
        return Scope.GetByText(string.Create(CultureInfo.InvariantCulture, $"Version v{version}"), new() { Exact = true });
    }
}
