using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Navigation;

[Area(Area.Navigation)]
public sealed class NavigationTests : PlatformTest
{
    // From the annotations, the navigation (the docked sidebar on a wide screen, the slideover behind "Open
    // navigation" on a phone or a tablet) opens pages of the view, the project and outside, and back: each with its
    // route and its own content.
    [Test]
    [Elementary]
    [TestCase("6")]
    public async Task Navigation_MobileMenu_OpensPages()
    {
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();

        (string Link, string Path, ILocator Content)[] pages =
        [
            ("Configurations", Routes.Configurations, new ConfigurationsPage(App, Routes).Filter),
            ("Values store", Routes.KeyValues, new KeyValuesPage(App, Routes).Search),
            ("Settings", Routes.Settings, new ProjectSettingsPage(App, Routes).ProjectHeading),
            ("Organizations", Routes.Organizations, new OrganizationsPage(App, Routes).Heading),
            ("Annotations", Routes.Annotations, annotations.Filter),
        ];

        foreach (var (link, path, content) in pages)
        {
            await App.NavigateAsync(link);
            await App.ExpectAtAsync(path);
            await Expect(content).ToBeVisibleAsync();
        }
    }
}
