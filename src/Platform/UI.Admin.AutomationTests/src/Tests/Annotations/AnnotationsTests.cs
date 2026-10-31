using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation.Runtime.TUnit;
using _42.Testing.UI.Automation.Runtime.Users;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Annotations;

[Area(Area.Annotations)]
public sealed class AnnotationsTests : PlatformTest
{
    // Bulk create is Contributor and up.
    protected override UserRequirement RequiredUser => EditingUser;

    // Bulk create (the toolbar on a wide screen, the command palette on a phone) creates a new responsibility; the grid
    // (the list on a phone) filtered by its key shows it, and its detail, opened by the URL, names it. Both are
    // virtualized, so the test looks at the filtered result only.
    [Test]
    [Elementary]
    public async Task Annotations_CreateResponsibility_AppearsInGrid()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();

        var bulkCreate = await annotations.OpenBulkCreateAsync();
        await bulkCreate.EnterKeysAsync(key);
        await Expect(bulkCreate.Summary).ToHaveTextAsync("1 new");
        await Expect(bulkCreate.ReviewRow(key)).ToContainTextAsync("New");
        await bulkCreate.CreateAsync(1);
        await Expect(bulkCreate.Result).ToHaveTextAsync("Created 1 annotation.");
        await bulkCreate.CloseAsync();

        await annotations.FilterAsync($"key:{key}");
        await Expect(annotations.Item(key)).ToBeVisibleAsync();

        var detail = new AnnotationDetail(App, Routes);
        await detail.OpenAsync(key);
        await Expect(detail.Identity).ToHaveTextAsync(key);
    }
}
