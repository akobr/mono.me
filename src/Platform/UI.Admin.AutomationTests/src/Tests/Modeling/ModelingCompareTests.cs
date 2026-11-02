using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Modeling;

// Compare views: the effective configuration of a key in this view against another view of the project. A view is a
// project-wide change, so the test works in a disposable project, which starts with the default view only.
[Area(Area.Modeling)]
public sealed class ModelingCompareTests : PlatformTest
{
    // The diff of two configurations that differ in one line.
    private const string OneChange = "+1 −1 · 1 change";

    // MOD-CMP-01: Compare views is disabled without another view, then preselects it and shows the difference.
    // A new project has only its default view: the tab Compare views is disabled, and its address says there is no
    // other view. Once a second view exists and a new responsibility has a configuration in both views (a different
    // atCompare, the same shared), the tab is enabled and preselects the only other view (?to=). The picked key shows
    // the diff of the two effective documents in one changed line, and a ?to= that names the view itself is replaced by
    // the other view: a view is never compared with itself.
    [Test]
    [TestCase(208)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task CompareViews_OtherView_PreselectedAndDiffed()
    {
        var project = await CreateDisposableProjectAsync();
        var modeling = new ModelingPage(App, project);
        await modeling.OpenAsync("schemas");
        await Expect(modeling.Tab("Compare views")).ToBeDisabledAsync();
        await Expect(modeling.Tab("Schemas")).ToBeEnabledAsync();

        var compare = new ModelingComparePage(App, project);
        await compare.OpenAsync();
        await Expect(compare.NoOtherView).ToBeVisibleAsync();
        await Expect(compare.NoOtherViewDescription).ToBeVisibleAsync();
        await Expect(compare.PickPrompt).ToHaveCountAsync(0);

        var views = new ViewsPage(App, project);
        await views.OpenAsync();
        var other = await views.CreateViewAsync(PlatformData.ViewName());

        var key = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var here = $"{PlatformData.RunSlug}-default";
        var there = $"{PlatformData.RunSlug}-other";
        await ConfigureAsync(project, key, here);
        await ConfigureAsync(other, key, there);

        // The key picker lists the keys with a configuration when the page loads, hence a new load after the setup.
        await compare.OpenAsync();
        await Expect(modeling.Tab("Compare views")).ToBeEnabledAsync();
        await App.ExpectQueryAsync("to", other.View);
        await Expect(compare.ViewPicker).ToHaveTextAsync(other.View);
        await Expect(compare.PickPrompt).ToBeVisibleAsync();
        await Expect(compare.KeyPicker).ToHaveTextAsync("Pick a key");

        await compare.PickKeyAsync(key);
        await Expect(compare.KeyPicker).ToHaveTextAsync(key);
        await ExpectDiffAsync(compare, project.View, other.View, here, there);

        await compare.OpenAsync(to: project.View, key: key);
        await App.ExpectQueryAsync("to", other.View);
        await App.ExpectQueryAsync("key", key);
        await Expect(compare.ViewPicker).ToHaveTextAsync(other.View);
        await ExpectDiffAsync(compare, project.View, other.View, here, there);
    }

    // The diff of the key's effective documents, this view's with the other view's: one changed line, the view's value
    // on the left and the other's on the right under the views' names from lg; below it one unified editor with both.
    private async Task ExpectDiffAsync(ModelingComparePage compare, string view, string otherView, string here, string there)
    {
        await Expect(compare.DiffSummary).ToHaveTextAsync(OneChange);
        if (Page.ViewportSize is not { Width: < ModelingComparePage.SplitDiffWidth })
        {
            await Expect(compare.DiffEditors).ToHaveCountAsync(2);
            await Expect(compare.DiffEditors.Nth(0)).ToContainTextAsync($"\"atCompare\": \"{here}\"");
            await Expect(compare.DiffEditors.Nth(1)).ToContainTextAsync($"\"atCompare\": \"{there}\"");
            var labels = compare.SideLabels(otherView);
            await Expect(labels.GetByText(view, new() { Exact = true })).ToBeVisibleAsync();
            await Expect(labels.GetByText(otherView, new() { Exact = true })).ToBeVisibleAsync();
        }
        else
        {
            await Expect(compare.DiffEditors).ToHaveCountAsync(1);
            await Expect(compare.DiffEditors).ToContainTextAsync($"\"atCompare\": \"{here}\"");
            await Expect(compare.DiffEditors).ToContainTextAsync($"\"atCompare\": \"{there}\"");
        }
    }

    // Bulk creates the key in the view of the routes and saves {"atCompare": <value>, "shared": 1} as its own
    // configuration (value: a slug, no JSON escapes): Create empty (v1, "Configuration created"), then the document (v2).
    private async Task ConfigureAsync(AppRoutes routes, string key, string value)
    {
        var annotations = new AnnotationsPage(App, routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(key);

        var configuration = new ConfigurationDetail(App, routes);
        await configuration.OpenAsync(key);
        await configuration.CreateEmptyAsync();
        await Expect(App.Toast("Configuration created")).ToBeVisibleAsync();
        await configuration.SetDocumentAsync($$"""{"atCompare":"{{value}}","shared":1}""");
        await configuration.SaveAsync(2);
    }
}
