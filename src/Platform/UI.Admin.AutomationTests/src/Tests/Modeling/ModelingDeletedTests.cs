using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Modeling;

// Deleted: the deleted annotations of the view, with the roots of deletions and Show cascaded, the read-only details,
// restore with and without descendants, bulk restore and Delete permanently. Each test bulk creates its own subjects
// (with a context below some of them) and deletes them in the Annotations section; a deployed view lists other
// deletions too, so the rows are always found by the test's keys and "Select all" is never used.
[Area(Area.Modeling)]
public sealed class ModelingDeletedTests : PlatformTest
{
    // From this width the table shows its Descendants column.
    private const int DescendantsColumnWidth = 768;

    // MOD-DEL-01: Deleted lists the root of a deletion and Show cascaded adds its descendants.
    // Deleting a subject deletes its context with it, but the list shows only the subject, the root of the deletion,
    // with one descendant. Show cascaded (?cascaded=1) lists the context too, with the subject as its root and the
    // badge "root" on the subject; off again, the context is gone. The details of the subject show it read-only with
    // the banner of a deleted annotation, and close with Escape.
    [Test]
    [TestCase(196)]
    [UserRole(Role.Editor)]
    public async Task Deleted_ShowCascaded_ListsDescendants()
    {
        var context = PlatformData.AnnotationKey(PlatformData.Context);
        var subject = PlatformData.AncestorKey(context, PlatformData.Subject);
        await CreateAndDeleteAsync([context], [subject]);

        var deleted = new ModelingDeletedPage(App, Routes);
        await deleted.OpenAsync();
        await Expect(deleted.Row(subject)).ToBeVisibleAsync();
        await Expect(deleted.Row(context)).ToHaveCountAsync(0);
        if (Page.ViewportSize is not { Width: < DescendantsColumnWidth })
        {
            await Expect(await deleted.CellAsync(subject, "Descendants")).ToHaveTextAsync("1");
        }

        await deleted.SetShowCascadedAsync(true);
        await Expect(deleted.RootBadge(subject)).ToBeVisibleAsync();
        await Expect(await deleted.CellAsync(context, "Root")).ToHaveTextAsync(subject);

        await deleted.SetShowCascadedAsync(false);
        await Expect(deleted.Row(subject)).ToBeVisibleAsync();
        await Expect(deleted.Row(context)).ToHaveCountAsync(0);

        await deleted.ShowButton(subject).ClickAsync();
        var details = deleted.Details(subject);
        await Expect(details).ToBeVisibleAsync();
        await Expect(details).ToHaveAccessibleDescriptionAsync(ModelingDeletedPage.DetailsDescriptionText);
        await Expect(deleted.DeletedAnnotationBanner(subject)).ToBeVisibleAsync();
        await Expect(deleted.AnnotationDocument(subject)).ToContainTextAsync(subject);

        await Page.Keyboard.PressAsync("Escape");
        await Expect(details).ToBeHiddenAsync();
        await Expect(deleted.Row(subject)).ToBeVisibleAsync();
    }

    // MOD-DEL-02: Restore a deletion with and without its descendants.
    // Two subjects are deleted with their contexts. Restore brings the first back with its context; Restore without
    // descendants brings the second back alone, so its context stays deleted and becomes a root of its own. The first
    // subject's context is an annotation of the view again.
    [Test]
    [TestCase(199)]
    [UserRole(Role.Editor)]
    public async Task Deleted_Restore_WithAndWithoutDescendants()
    {
        var firstContext = PlatformData.AnnotationKey(PlatformData.Context);
        var firstSubject = PlatformData.AncestorKey(firstContext, PlatformData.Subject);
        var secondContext = PlatformData.AnnotationKey(PlatformData.Context);
        var secondSubject = PlatformData.AncestorKey(secondContext, PlatformData.Subject);
        await CreateAndDeleteAsync([firstContext, secondContext], [firstSubject, secondSubject]);

        var deleted = new ModelingDeletedPage(App, Routes);
        await deleted.OpenAsync();
        await Expect(deleted.Row(firstSubject)).ToBeVisibleAsync();
        await Expect(deleted.Row(secondSubject)).ToBeVisibleAsync();
        await Expect(deleted.Row(firstContext)).ToHaveCountAsync(0);
        await Expect(deleted.Row(secondContext)).ToHaveCountAsync(0);

        await deleted.RowActionAsync(firstSubject, "Restore");
        await Expect(App.Toast($"{firstSubject} restored")).ToBeVisibleAsync();
        await Expect(deleted.Row(firstSubject)).ToHaveCountAsync(0);

        await deleted.RowActionAsync(secondSubject, "Restore without descendants");
        await Expect(App.Toast($"{secondSubject} restored")).ToBeVisibleAsync();
        await Expect(deleted.Row(secondSubject)).ToHaveCountAsync(0);
        await Expect(deleted.Row(secondContext)).ToBeVisibleAsync();

        await deleted.SetShowCascadedAsync(true);
        await Expect(deleted.RootBadge(secondContext)).ToBeVisibleAsync();
        await Expect(deleted.Row(firstSubject)).ToHaveCountAsync(0);
        await Expect(deleted.Row(firstContext)).ToHaveCountAsync(0);

        await ExpectRestoredAsync(deleted, firstContext);
    }

    // MOD-DEL-03: Bulk restore of selected deletions.
    // Selecting two deleted subjects shows the toolbar "Bulk actions" with "2 selected"; Clear selection unchecks them
    // and hides it. Selected again, the toolbar's Restore restores both at once ("2 annotations restored"), and they
    // are annotations of the view again.
    [Test]
    [TestCase(201)]
    [UserRole(Role.Editor)]
    public async Task Deleted_BulkRestore_RestoresSelection()
    {
        var first = PlatformData.AnnotationKey(PlatformData.Subject);
        var second = PlatformData.AnnotationKey(PlatformData.Subject);
        await CreateAndDeleteAsync([first, second], [first, second]);

        var deleted = new ModelingDeletedPage(App, Routes);
        await deleted.OpenAsync();
        await deleted.SelectCheckbox(first).CheckAsync();
        await deleted.SelectCheckbox(second).CheckAsync();
        await Expect(deleted.BulkActions).ToBeVisibleAsync();
        await Expect(deleted.SelectedCount(2)).ToBeVisibleAsync();

        await deleted.ClearSelectionButton.ClickAsync();
        await Expect(deleted.BulkActions).ToBeHiddenAsync();
        await Expect(deleted.SelectCheckbox(first)).Not.ToBeCheckedAsync();
        await Expect(deleted.SelectCheckbox(second)).Not.ToBeCheckedAsync();

        await deleted.SelectCheckbox(first).CheckAsync();
        await deleted.SelectCheckbox(second).CheckAsync();
        await Expect(deleted.SelectedCount(2)).ToBeVisibleAsync();
        await deleted.BulkRestoreButton.ClickAsync();
        await Expect(App.Toast("2 annotations restored")).ToBeVisibleAsync();
        await Expect(deleted.Row(first)).ToHaveCountAsync(0);
        await Expect(deleted.Row(second)).ToHaveCountAsync(0);
        await Expect(deleted.BulkActions).ToBeHiddenAsync();

        await ExpectRestoredAsync(deleted, first);
    }

    // MOD-DEL-04: Delete permanently needs the typed key, or the typed count for several.
    // An Administrator deletes a deleted subject with its context permanently: the confirmation names the descendant
    // and is enabled only with the typed key; afterwards neither the subject nor (with Show cascaded) its context is
    // listed. Two selected subjects are deleted permanently with the typed count. Only the test's own deletions are
    // purged.
    [Test]
    [TestCase(203)]
    [UserRole(Role.Admin)]
    public async Task Deleted_DeletePermanently_NeedsTypedConfirmation()
    {
        var context = PlatformData.AnnotationKey(PlatformData.Context);
        var subject = PlatformData.AncestorKey(context, PlatformData.Subject);
        var first = PlatformData.AnnotationKey(PlatformData.Subject);
        var second = PlatformData.AnnotationKey(PlatformData.Subject);
        await CreateAndDeleteAsync([context, first, second], [subject, first, second]);

        var deleted = new ModelingDeletedPage(App, Routes);
        await deleted.OpenAsync();
        await deleted.RowActionAsync(subject, ModelingDeletedPage.DeletePermanentlyLabel);
        var purge = deleted.PurgeDialog(subject);
        await purge.ExpectOpenAsync();
        await Expect(purge.Description).ToHaveTextAsync("The annotation, its 1 descendant and their change history are removed for good. Their configuration versions stay until they expire.");
        await Expect(purge.Button(ModelingDeletedPage.DeletePermanentlyLabel)).ToBeDisabledAsync();
        await Expect(purge.ConfirmationInput).ToHaveAccessibleNameAsync($"Type {subject} to confirm");

        await purge.TypeConfirmationAsync(subject);
        await Expect(purge.Button(ModelingDeletedPage.DeletePermanentlyLabel)).ToBeEnabledAsync();
        await purge.ConfirmAsync(ModelingDeletedPage.DeletePermanentlyLabel);
        await Expect(App.Toast($"{subject} deleted permanently")).ToBeVisibleAsync();
        await Expect(deleted.Row(subject)).ToHaveCountAsync(0);

        await deleted.SetShowCascadedAsync(true);
        await Expect(deleted.Row(first)).ToBeVisibleAsync();
        await Expect(deleted.Row(subject)).ToHaveCountAsync(0);
        await Expect(deleted.Row(context)).ToHaveCountAsync(0);

        await deleted.SelectCheckbox(first).CheckAsync();
        await deleted.SelectCheckbox(second).CheckAsync();
        await Expect(deleted.SelectedCount(2)).ToBeVisibleAsync();
        await deleted.BulkDeletePermanentlyButton.ClickAsync();
        var purgeBoth = deleted.PurgeDialog(2);
        await purgeBoth.ExpectOpenAsync();
        await Expect(purgeBoth.Description).ToHaveTextAsync("The 2 annotations, their descendants and their change history are removed for good. Their configuration versions stay until they expire.");
        await Expect(purgeBoth.ConfirmationInput).ToHaveAccessibleNameAsync("Type 2 to confirm");

        await purgeBoth.TypeConfirmationAsync("2");
        await purgeBoth.ConfirmAsync(ModelingDeletedPage.DeletePermanentlyLabel);
        await Expect(App.Toast("2 annotations deleted permanently")).ToBeVisibleAsync();
        await Expect(deleted.Row(first)).ToHaveCountAsync(0);
        await Expect(deleted.Row(second)).ToHaveCountAsync(0);
    }

    // Bulk creates the keys (with their missing ancestors), then deletes each of the others in its detail (More
    // actions, Delete), which deletes its descendants with it.
    private async Task CreateAndDeleteAsync(string[] create, string[] delete)
    {
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(create);

        var detail = new AnnotationDetail(App, Routes);
        foreach (var key in delete)
        {
            await detail.OpenAsync(key);
            await Expect(detail.MoreActionsButton).ToBeVisibleAsync();
            await detail.DeleteAsync(key);
        }
    }

    // The annotation is in the view again: its detail shows its identity and Title, without the banner of a deleted
    // annotation.
    private async Task ExpectRestoredAsync(ModelingDeletedPage deleted, string key)
    {
        var detail = new AnnotationDetail(App, Routes);
        await detail.OpenAsync(key);
        await App.ExpectAtAsync(Routes.Annotation(key));
        await Expect(detail.Identity).ToContainTextAsync(key);
        await Expect(detail.Title).ToBeVisibleAsync();
        await Expect(deleted.DeletedAnnotationBanner(key)).ToHaveCountAsync(0);
    }
}
