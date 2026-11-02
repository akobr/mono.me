using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Annotations;

// Role Access: what a Reader and a user who is exactly Contributor may do with annotations. The area hides what a
// role may not do (no lock texts): the create actions, the bulk bar, the save bar and Delete need Contributor.
[Area(Area.Annotations)]
public sealed partial class AnnotationRoleTests : PlatformTest
{
    // ANN-ROL-01: A Reader gets no create actions, not even in an empty view.
    // In the Reader's project the annotations page has its filter but neither New, New annotation nor Bulk create, in
    // the navbar or in an empty state; a view nobody wrote to shows the empty state with its text and no buttons. The
    // test writes nothing, so the view stays unregistered.
    [Test]
    [TestCase(223)]
    [UserRole(Role.Reader)]
    public async Task AnnotationAccess_Reader_HasNoCreateActions()
    {
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await Expect(annotations.Filter).ToBeVisibleAsync();
        await ExpectNoCreateActionsAsync();

        var empty = new AnnotationsPage(App, Routes with { View = PlatformData.ViewName() });
        await empty.OpenAsync();
        await Expect(empty.EmptyState).ToBeVisibleAsync();
        await Expect(empty.EmptyStateBlock.GetByText("Create the first annotations, or import them with the sform CLI.", new() { Exact = true })).ToBeVisibleAsync();
        await ExpectNoCreateActionsAsync();
        await Expect(empty.FilterCount).ToHaveTextAsync("0");
    }

    // ANN-ROL-02: Exactly a Contributor can create, edit and delete.
    // In the Contributor's project the create actions are there; Bulk create creates a subject, which the filter lists.
    // On the grid its row's checkbox brings the bulk bar, which Clear selection removes. The detail is editable (no
    // Read-only badge) and saves a title, and Delete removes the subject from the list.
    [Test]
    [TestCase(225)]
    [UserRole(Role.Contributor)]
    public async Task AnnotationAccess_Contributor_CreatesEditsDeletes()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Subject);
        var title = PlatformData.Title();
        var annotations = new AnnotationsPage(App, Routes);
        var detail = new AnnotationDetail(App, Routes);
        var form = new AnnotationForm(App);
        var newAnnotation = new NewAnnotationDialog(App);
        await annotations.OpenAsync();

        if (App.IsPhone)
        {
            await Expect(newAnnotation.NewAnnotationButton).ToBeVisibleAsync();
        }
        else
        {
            await Expect(annotations.BulkCreateButton).ToBeVisibleAsync();
            await Expect(newAnnotation.NewButton).ToBeVisibleAsync();
        }

        var bulkCreate = await annotations.OpenBulkCreateAsync();
        await bulkCreate.EnterKeysAsync(key);
        await bulkCreate.CreateAsync(1);
        await Expect(bulkCreate.Result).ToHaveTextAsync("Created 1 annotation.");
        await bulkCreate.CloseAsync();

        await annotations.FilterAsync($"key:{key}");
        await Expect(annotations.Item(key)).ToBeVisibleAsync();
        if (Page.ViewportSize is not { Width: < AnnotationsPage.GridWidth })
        {
            await annotations.RowCheckbox(key).ClickAsync();
            await Expect(annotations.BulkBar.SelectedCount).ToHaveTextAsync("1 selected");
            await annotations.BulkBar.ClearSelectionButton.ClickAsync();
            await Expect(annotations.BulkBar.Root).ToHaveCountAsync(0);
        }

        await annotations.OpenAnnotationFromListAsync(key);
        await Expect(detail.Identity).ToHaveTextAsync(key);
        await Expect(form.ReadOnlyBadge).ToHaveCountAsync(0);
        await form.Title.FillAsync(title);
        await form.SaveButton.ClickAsync();
        await Expect(App.Toast("Annotation saved")).ToBeVisibleAsync();

        await detail.DeleteAsync(key);
        await Expect(annotations.Item(key)).ToHaveCountAsync(0);
        await Expect(annotations.NoMatchingAnnotations).ToBeVisibleAsync();
    }

    // ANN-ROL-03: A Reader sees an existing annotation read-only, without a bulk bar.
    // The seed of the mock's Reader project lists annotations. A Reader may check a row of the grid but gets no bulk
    // bar; the first annotation's detail has the Read-only badge, disabled fields and no save bar or Add value, a
    // read-only JSON document, and no Delete under More actions.
    [Test]
    [TestCase(226)]
    [UserRole(Role.Reader)]
    [MockOnly("the seed annotations of the Reader project, which a Reader cannot create")]
    public async Task AnnotationAccess_Reader_SeesDetailReadOnly()
    {
        var annotations = new AnnotationsPage(App, Routes);
        var detail = new AnnotationDetail(App, Routes);
        var form = new AnnotationForm(App);
        var grid = Page.ViewportSize is not { Width: < AnnotationsPage.GridWidth };
        await annotations.OpenAsync();
        await Expect(grid ? annotations.AnnotationGrid : annotations.AnnotationList).ToBeVisibleAsync();
        await Expect(annotations.AnnotationRows.First).ToBeVisibleAsync();
        await Expect(annotations.EmptyState).ToHaveCountAsync(0);

        var first = annotations.AnnotationRows.First;
        if (grid)
        {
            var checkbox = first.GetByRole(AriaRole.Checkbox);
            await checkbox.ClickAsync();
            await Expect(checkbox).ToBeCheckedAsync();
            await Expect(annotations.BulkBar.Root).ToHaveCountAsync(0);

            // The pinned columns lead: select, Type, Name.
            await first.GetByRole(AriaRole.Gridcell).Nth(2).ClickAsync();
        }
        else
        {
            await first.GetByRole(AriaRole.Button).ClickAsync();
        }

        await Expect(Page).ToHaveURLAsync(AnnotationDetailUrl());
        var key = Uri.UnescapeDataString(AnnotationDetailUrl().Match(Page.Url).Groups["key"].Value);
        await Expect(detail.Identity).ToHaveTextAsync(key);

        await Expect(form.ReadOnlyBadge).ToBeVisibleAsync();
        await Expect(form.Title).ToBeDisabledAsync();
        await Expect(form.Labels).ToBeDisabledAsync();
        await Expect(form.SaveButton).ToHaveCountAsync(0);
        await Expect(form.RevertButton).ToHaveCountAsync(0);
        await Expect(form.AddValueButton).ToHaveCountAsync(0);

        await form.SelectModeAsync("JSON");
        await App.ExpectQueryAsync("format", "json");
        await Expect(form.Document.Content).Not.ToBeEditableAsync();
        await Expect(form.ReadOnlyBadge).ToBeVisibleAsync();

        await detail.MoreActionsButton.ClickAsync();
        foreach (var item in new[] { "Show in graph", "Copy link", "Copy key" })
        {
            await Expect(detail.MenuItem(item)).ToBeVisibleAsync();
        }

        await Expect(detail.MenuItem("Delete")).ToHaveCountAsync(0);
    }

    // None of the create actions of the annotations page: New (from 640 px), New annotation (a phone's navbar, an
    // empty state) and Bulk create (the navbar from 640 px, an empty state).
    private async Task ExpectNoCreateActionsAsync()
    {
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "New", Exact = true })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "New annotation", Exact = true })).ToHaveCountAsync(0);
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Bulk create", Exact = true })).ToHaveCountAsync(0);
    }

    // The detail of an annotation in the list's view, …/annotations/<key>, with any query.
    [GeneratedRegex(@"/annotations/(?<key>[^/?#]+)(?:[?#].*)?$")]
    private static partial Regex AnnotationDetailUrl();
}
