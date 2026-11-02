using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Configurations;

// Inheritance: values a configuration inherits from its ancestors. Each test Bulk creates a unit, which creates its
// missing responsibility, the unit's only parent: the Effective tab merges the responsibility's configuration under the
// unit's own (and keeps it after Delete own), Composition names the step that set each line, and the header's
// breadcrumb, Lineage and Annotation lead to the parent and to the annotation. A deployed target's Responsibility or
// Unit template could add values and steps, so the tests look for their own values and read the steps' orders.
[Area(Area.Configurations)]
public sealed class ConfigurationInheritanceTests : PlatformTest
{
    // CFG-INH-01: A child's Effective merges the parent's configuration and keeps it after Delete own.
    // The responsibility stores shared and limit 1, the unit only limit 2: the unit's Edit tab holds its own content,
    // its Effective tab the merge, where the unit's limit wins. Once the unit's own configuration is deleted, Effective
    // shows the responsibility's values alone.
    [Test]
    [TestCase(187)]
    [UserRole(Role.Editor)]
    public async Task Configurations_Effective_MergesParentConfiguration()
    {
        var (unit, parent) = await ConfigurationSteps.CreateUnitAsync(App, Routes, PlatformData);
        var shared = $"\"shared\": \"{PlatformData.RunSlug}\"";

        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenAsync(parent);
        await ConfigurationSteps.StoreVersionsAsync(App, configuration, ParentDocument());

        await configuration.OpenAsync(unit);
        await ConfigurationSteps.StoreVersionsAsync(App, configuration, """{"limit": 2}""");
        await Expect(configuration.Editor).ToContainTextAsync("\"limit\": 2");
        await Expect(configuration.Editor).Not.ToContainTextAsync("\"shared\"");

        await configuration.OpenTabAsync("Effective");
        var effective = configuration.EffectiveView;
        await Expect(effective.MergeOrder).ToBeVisibleAsync();
        await Expect(effective.Document.Content).ToContainTextAsync(shared);
        await Expect(effective.Document.Content).ToContainTextAsync("\"limit\": 2");
        await Expect(effective.Document.Content).Not.ToContainTextAsync("\"limit\": 1");

        await configuration.OpenEditTabAsync(unit);
        var confirmation = configuration.DeleteOwnDialog(unit);
        await configuration.DeleteOwnButton.ClickAsync();
        await confirmation.ExpectOpenAsync();
        await confirmation.TypeConfirmationAsync(unit);
        await confirmation.ConfirmAsync("Delete");
        await Expect(App.Toast("Own configuration deleted; inherited values remain")).ToBeVisibleAsync();
        await Expect(configuration.NoConfigurationHeading).ToBeVisibleAsync();

        // Admin UI bug (424-152 review): ConfigurationEditor.vue remove() leaves the draft {} while nothing is stored, so
        // another tab may ask "Discard changes?" after Delete own; discard it only when it shows.
        await configuration.EffectiveTab.ClickAsync();
        await new DiscardChangesDialog(App).DiscardIfShownAsync(effective.Document.Content);
        await App.ExpectAtAsync(Routes.ConfigurationTab(unit, "effective"));
        await Expect(effective.Document.Content).ToContainTextAsync(shared);
        await Expect(effective.Document.Content).ToContainTextAsync("\"limit\": 1");
    }

    // CFG-INH-02: Composition shows the step that set each line and links the parent.
    // The unit's composition lists the responsibility's configuration (a link) and the unit's own content as steps;
    // the gutter marks shared with the responsibility's step and limit with the unit's, the toggle of the
    // responsibility's step names its first line in the status, its link opens the parent's Edit tab with the filter,
    // and Document drops the composition again.
    [Test]
    [TestCase(192)]
    [UserRole(Role.Editor)]
    public async Task Configurations_Composition_ShowsStepOfEachLine()
    {
        var (unit, parent) = await ConfigurationSteps.CreateUnitAsync(App, Routes, PlatformData);
        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenAsync(parent);
        await ConfigurationSteps.StoreVersionsAsync(App, configuration, ParentDocument());
        await configuration.OpenAsync(unit);
        await ConfigurationSteps.StoreVersionsAsync(App, configuration, """{"limit": 2}""");

        await App.GotoAsync(AppRoutes.WithQuery(Routes.ConfigurationTab(unit, "effective"), ("q", PlatformData.RunSlug)));
        var effective = configuration.EffectiveView;
        await Expect(effective.Document.Content).ToBeVisibleAsync();

        await effective.ShowCompositionAsync();
        await Expect(effective.StepLink(parent)).ToBeVisibleAsync();
        await Expect(effective.Step(unit)).ToBeVisibleAsync();
        await Expect(effective.StepLink(unit)).ToHaveCountAsync(0);

        var parentStep = await effective.StepOrderAsync(parent);
        var unitStep = await effective.StepOrderAsync(unit);
        await effective.ExpectLineMarkerAsync($"\"shared\": \"{PlatformData.RunSlug}\"", parentStep);
        await effective.ExpectLineMarkerAsync("\"limit\": 2", unitStep);

        var toggle = effective.StepToggle(parentStep);
        await toggle.ClickAsync();
        await Expect(toggle).ToHaveAttributeAsync("aria-pressed", "true");
        await Expect(effective.CompositionStatus).ToHaveTextAsync(new Regex($@"^Line \d+: step {Regex.Escape(parentStep)} · {Regex.Escape(parent)}$"));

        await effective.StepLink(parent).ClickAsync();
        await App.ExpectAtAsync(Routes.Configuration(parent));
        await App.ExpectQueryAsync("q", PlatformData.RunSlug);
        await App.ExpectQueryAsync("show", null);

        await App.Page.GoBackAsync();
        await App.ExpectAtAsync(Routes.ConfigurationTab(unit, "effective"));
        await App.ExpectQueryAsync("show", "composition");
        await Expect(effective.StepsHeading).ToBeVisibleAsync();
        await effective.ShowDocumentAsync();
        await Expect(effective.CompositionGutter).ToHaveCountAsync(0);
        await Expect(effective.Document.Content).ToContainTextAsync("\"limit\": 2");
    }

    // CFG-INH-03: The breadcrumb and Lineage open the parent's configuration, and Annotation opens the annotation.
    // Neither annotation has a configuration: the unit's breadcrumb links its responsibility, Lineage lists the
    // responsibility (level 1, a link) and the unit (level 2), and both open the responsibility's configuration with
    // the filter, where Lineage is disabled; Annotation opens the responsibility's annotation without the filter. The
    // type names are the built-in ones: a renamed type is a preference of the browser, and every test has a fresh one.
    [Test]
    [TestCase(210)]
    [UserRole(Role.Editor)]
    public async Task Configurations_BreadcrumbAndLineage_OpenParent()
    {
        var (unit, parent) = await ConfigurationSteps.CreateUnitAsync(App, Routes, PlatformData);
        var parentName = parent.Split('.')[^1];
        var unitName = unit.Split('.')[^1];

        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenFilteredAsync(unit, PlatformData.RunSlug);
        await Expect(configuration.BreadcrumbLink($"Responsibility {parentName}")).ToBeVisibleAsync();
        await Expect(configuration.BreadcrumbCurrent).ToHaveTextAsync($"Unit {unitName}");

        await configuration.LineageButton.ClickAsync();
        await Expect(configuration.LineagePopover).ToBeVisibleAsync();
        var levels = configuration.LineageLevels;
        await Expect(levels).ToHaveCountAsync(2);
        await Expect(levels.Nth(0).GetByText("1", new() { Exact = true })).ToBeVisibleAsync();
        var parentLink = levels.Nth(0).GetByRole(AriaRole.Link, new() { Name = parent, Exact = true });
        await Expect(parentLink).ToBeVisibleAsync();
        await Expect(levels.Nth(1).GetByText("2", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(levels.Nth(1).GetByText(unit, new() { Exact = true })).ToBeVisibleAsync();

        await parentLink.ClickAsync();
        await ExpectParentConfigurationAsync(configuration, parent);
        await Expect(configuration.LineageButton).ToBeDisabledAsync();
        await Expect(configuration.NoConfigurationHeading).ToBeVisibleAsync();

        await App.Page.GoBackAsync();
        await App.ExpectAtAsync(Routes.Configuration(unit));
        await configuration.BreadcrumbLink($"Responsibility {parentName}").ClickAsync();
        await ExpectParentConfigurationAsync(configuration, parent);

        await configuration.AnnotationLink.ClickAsync();
        await App.ExpectAtAsync(Routes.Annotation(parent));
        await App.ExpectQueryAsync("q", null);
        var annotation = new AnnotationDetail(App, Routes);
        await Expect(annotation.Panel.Root).ToBeVisibleAsync();
        await Expect(annotation.Heading).ToHaveTextAsync(parentName);
    }

    // The responsibility's document: a value only it sets and one the unit overrides.
    private string ParentDocument()
    {
        return $$"""{"shared": "{{PlatformData.RunSlug}}", "limit": 1}""";
    }

    // The responsibility's Edit tab opened, with the run's filter.
    private async Task ExpectParentConfigurationAsync(ConfigurationDetail configuration, string parent)
    {
        await App.ExpectAtAsync(Routes.Configuration(parent));
        await App.ExpectQueryAsync("q", PlatformData.RunSlug);
        await Expect(configuration.Region(parent)).ToBeVisibleAsync();
    }
}
