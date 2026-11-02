using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Annotations;

// Delete and Restore: the soft deletion from the detail with its descendants and Undo, the detail of a deleted key
// with its banner and its two restores, and a key that does not exist.
[Area(Area.Annotations)]
public sealed partial class AnnotationDeleteTests : PlatformTest
{
    // A date as the app writes it by default, yyyy-mm-dd.
    private const string DatePattern = @"\d{4}-\d{2}-\d{2}";

    // ANN-DEL-01: Delete from the detail with descendants, then Undo.
    // An execution's responsibility has two descendants (the usage and the execution), which its Delete confirms and
    // deletes with it; the toast says so with the date until which it can be restored, and the responsibility's filter
    // finds nothing. Undo within the toast's 10 seconds restores all three and reopens the detail, and the history
    // records the deletion and the restore.
    [Test]
    [TestCase(220)]
    [UserRole(Role.Editor)]
    public async Task AnnotationDelete_WithDescendants_UndoRestores()
    {
        var execution = PlatformData.AnnotationKey(PlatformData.Execution);
        var responsibility = PlatformData.AncestorKey(execution, PlatformData.Responsibility);
        var usage = PlatformData.AncestorKey(execution, PlatformData.Usage);
        var filter = $"responsibility:{responsibility.Split('.')[1]}";
        var annotations = new AnnotationsPage(App, Routes);
        var detail = new AnnotationDetail(App, Routes);
        var history = new AnnotationHistory(App);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(execution);
        await annotations.WaitForCatalogAsync();

        // The confirmation counts the descendants in the loaded catalog: its exact count shows once it loaded.
        await detail.OpenFormAsync(responsibility);
        await Expect(detail.RelationsBlock.GetByRole(AriaRole.Link, new() { Name = "All descendants (2)", Exact = true })).ToBeVisibleAsync();

        await detail.MoreActionsAsync("Delete");
        var confirm = detail.DeleteDialog(responsibility);
        await confirm.ExpectOpenAsync();
        await Expect(confirm.Description).ToHaveTextAsync(
            $"{responsibility} and its 2 descendants move to Deleted, with their configuration. You can restore them from Deleted for 90 days.");
        await Expect(confirm.CancelButton).ToBeVisibleAsync();
        await Expect(confirm.Button("Delete")).ToBeVisibleAsync();

        var deleted = $"{responsibility} deleted";
        await confirm.ConfirmAsync("Delete");
        await Expect(App.Toast(deleted)).ToBeVisibleAsync();
        await Expect(ToastOf(deleted)).ToContainTextAsync(DeletionDescription());
        await Expect(App.ToastAction(deleted, "Undo")).ToBeVisibleAsync();
        await App.ExpectAtAsync(Routes.Annotations);

        await annotations.FilterAsync(filter);
        await Expect(annotations.NoMatchingAnnotations).ToBeVisibleAsync();

        await App.ToastAction(deleted, "Undo").ClickAsync();
        await Expect(App.Toast($"{responsibility} restored")).ToBeVisibleAsync();
        await App.ExpectAtAsync(Routes.Annotation(responsibility));
        await Expect(detail.Identity).ToHaveTextAsync(responsibility);

        await detail.CloseAsync();
        await annotations.FilterAsync(filter);
        await Expect(annotations.AnnotationRows).ToHaveCountAsync(3);
        foreach (var key in new[] { responsibility, usage, execution })
        {
            await Expect(annotations.Item(key)).ToBeVisibleAsync();
        }

        await App.GotoAsync(Routes.AnnotationHistory(responsibility));
        await Expect(history.Region).ToBeVisibleAsync();
        await Expect(AnnotationHistory.ActionBadge(history.Region, "Deleted")).ToBeVisibleAsync();
        await Expect(AnnotationHistory.ActionBadge(history.Region, "Restored")).ToBeVisibleAsync();
    }

    // ANN-DEL-02: A deleted key's detail shows the banner and restores with or without descendants.
    // A subject deleted with its context opens with the badge Deleted and the banner of its deletion (who, with how many
    // descendants, until when), without the tabs and without Delete; its context's banner names the subject it went
    // with. Restoring the subject without descendants leaves the context deleted as a root of its own, which Restore then
    // brings back. A key that never existed is not found.
    [Test]
    [TestCase(222)]
    [UserRole(Role.Editor)]
    public async Task AnnotationDelete_DeletedDetail_RestoresFromBanner()
    {
        var context = PlatformData.AnnotationKey(PlatformData.Context);
        var subject = PlatformData.AncestorKey(context, PlatformData.Subject);
        var missing = $"{subject}-missing";
        var annotations = new AnnotationsPage(App, Routes);
        var detail = new AnnotationDetail(App, Routes);
        var banner = new DeletedAnnotationBanner(App);
        await annotations.OpenAsync();
        var user = Regex.Escape(await App.SignedInNameAsync());
        await annotations.BulkCreateAsync(context);
        await detail.OpenFormAsync(subject);
        await detail.DeleteAsync(subject);

        await detail.OpenAsync(subject);
        await Expect(banner.Title(subject)).ToBeVisibleAsync();
        await Expect(detail.DeletedBadge).ToBeVisibleAsync();
        await Expect(banner.Description).ToHaveTextAsync(
            new Regex($@"^\s*Deleted on {DatePattern} by {user}\s+with 1 descendant\.\s+Purged automatically on {DatePattern}\.\s*$"));
        await Expect(detail.AnnotationTabLink).ToHaveCountAsync(0);
        await Expect(detail.HistoryTabLink).ToHaveCountAsync(0);
        await detail.MoreActionsButton.ClickAsync();
        await Expect(detail.MenuItem("Copy link")).ToBeVisibleAsync();
        await Expect(detail.MenuItem("Delete")).ToHaveCountAsync(0);
        await Page.Keyboard.PressAsync("Escape");
        await Expect(detail.MenuItem("Copy link")).ToHaveCountAsync(0);

        await detail.OpenAsync(context);
        await Expect(banner.Title(context)).ToBeVisibleAsync();
        await Expect(banner.Description).ToHaveTextAsync(
            new Regex($@"^\s*Deleted on {DatePattern} by {user}\s+together with {Regex.Escape(subject)}\.\s+Purged automatically on {DatePattern}\.\s*$"));

        await detail.OpenAsync(subject);
        await Expect(banner.Title(subject)).ToBeVisibleAsync();
        await banner.RestoreWithoutDescendantsAsync(subject);
        await Expect(detail.Identity).ToHaveTextAsync(subject);

        await detail.OpenAsync(context);
        await Expect(banner.Title(context)).ToBeVisibleAsync();
        await Expect(banner.Description).Not.ToContainTextAsync("together with");
        await Expect(banner.Description).ToHaveTextAsync(new Regex($@"^\s*Deleted on {DatePattern} by {user}\.\s+Purged automatically on {DatePattern}\.\s*$"));
        await banner.RestoreAsync(context);
        await Expect(detail.Identity).ToHaveTextAsync(context);

        await detail.OpenAsync(missing);
        await Expect(detail.NotFound).ToBeVisibleAsync();
        await Expect(detail.Panel.Root.GetByText($"{missing} does not exist in this view. It may have been deleted permanently.", new() { Exact = true })).ToBeVisibleAsync();
    }

    // The newest toast of the title, with its description and its actions.
    private ILocator ToastOf(string title)
    {
        return App.Notifications.Locator("li").Filter(new() { Has = Page.GetByText(title, new() { Exact = true }) }).Last;
    }

    // The description of a detail's delete with two descendants.
    [GeneratedRegex(@"With 2 descendants\. Restorable from Deleted until \d{4}-\d{2}-\d{2}\.")]
    private static partial Regex DeletionDescription();
}
