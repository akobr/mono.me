using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Annotations;

// New Annotation: the dialog "New annotation", its key preview, its checks and its time zone, and a create over a
// deleted key.
[Area(Area.Annotations)]
public sealed class AnnotationCreateTests : PlatformTest
{
    // ANN-NEW-01: New annotation creates an execution with its missing ancestors.
    // In a fresh browser the dialog starts at Execution in the project's default time zone, UTC. Three new names,
    // each through 'Create "<name>"', make the key, and the preview lists the four ancestors the API creates with it;
    // Create opens the new execution's detail with its title, as active and in UTC, and the subject's filter counts
    // the execution and its subject's three keys.
    [Test]
    [TestCase(69)]
    [UserRole(Role.Editor)]
    public async Task NewAnnotation_Execution_CreatesMissingAncestors()
    {
        var execution = PlatformData.AnnotationKey(PlatformData.Execution);
        var parts = execution.Split('.');
        var title = PlatformData.Title();
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();

        var dialog = await annotations.OpenNewAnnotationAsync();
        await Expect(dialog.TypeSelect).ToHaveTextAsync("Execution");
        await Expect(dialog.FillInTheNames).ToBeVisibleAsync();
        await Expect(dialog.TimeZone.Button).ToHaveTextAsync("UTC");

        await dialog.FillPartAsync("Subject", parts[1]);
        await dialog.FillPartAsync("Responsibility", parts[2]);
        await dialog.FillPartAsync("Context", parts[3]);
        await Expect(dialog.KeyPreview).ToHaveTextAsync(execution);
        var ancestors = string.Join(
            ", ",
            PlatformData.AncestorKey(execution, PlatformData.Usage),
            PlatformData.AncestorKey(execution, PlatformData.Context),
            PlatformData.AncestorKey(execution, PlatformData.Responsibility),
            PlatformData.AncestorKey(execution, PlatformData.Subject));
        await Expect(dialog.MissingAncestors).ToHaveTextAsync($"The API also creates the missing ancestors: {ancestors}");

        await dialog.TitleField.FillAsync(title);
        await dialog.CreateButton.ClickAsync();
        await Expect(App.Toast($"{execution} created")).ToBeVisibleAsync();
        await Expect(dialog.Root).ToBeHiddenAsync();

        var detail = new AnnotationDetail(App, Routes);
        await App.ExpectAtAsync(Routes.Annotation(execution));
        await Expect(detail.Identity).ToHaveTextAsync(execution);
        await Expect(detail.Title).ToHaveValueAsync(title);
        await Expect(detail.StatusBadge).ToHaveTextAsync("Active");
        await Expect(detail.TimeZoneButton).ToHaveTextAsync("UTC");

        await detail.CloseAsync();
        await annotations.WaitForCatalogAsync();
        await annotations.FilterAsync($"subject:{parts[1]}");
        await Expect(annotations.FilterCount).ToHaveTextAsync(AnnotationsPage.FilterCountOf(4));
    }

    // ANN-NEW-02: New annotation rejects dotted names and existing keys.
    // A name with a dot is refused with its message and makes no key; the name of an existing subject makes its key,
    // which the dialog marks as existing; both keep Create disabled. A context of the existing subject, picked from the
    // suggestions, needs no missing ancestor and can be created, and Cancel leaves it uncreated.
    [Test]
    [TestCase(71)]
    [UserRole(Role.Editor)]
    public async Task NewAnnotation_InvalidOrExistingKey_DisablesCreate()
    {
        var subjectKey = PlatformData.AnnotationKey(PlatformData.Subject);
        var subject = subjectKey.Split('.')[1];
        var context = PlatformData.AnnotationPart();
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(subjectKey);
        await annotations.WaitForCatalogAsync();

        var dialog = await annotations.OpenNewAnnotationAsync();
        await dialog.SelectTypeAsync("Subject");
        await dialog.NameField.FillAsync($"{PlatformData.AnnotationPart()}.{PlatformData.AnnotationPart()}");
        await Expect(dialog.PartError("Names cannot contain dots.")).ToBeVisibleAsync();
        await Expect(dialog.FillInTheNames).ToBeVisibleAsync();
        await Expect(dialog.CreateButton).ToBeDisabledAsync();

        await dialog.NameField.FillAsync(subject);
        await Expect(dialog.KeyPreview).ToHaveTextAsync(subjectKey);
        await Expect(dialog.AlreadyExists).ToBeVisibleAsync();
        await Expect(dialog.CreateButton).ToBeDisabledAsync();

        await dialog.SelectTypeAsync("Context");
        await dialog.PickExistingPartAsync("Subject", subject);
        await dialog.NameField.FillAsync(context);
        var contextKey = $"{PlatformData.Context}.{subject}.{context}";
        await Expect(dialog.KeyPreview).ToHaveTextAsync(contextKey);
        await Expect(dialog.MissingAncestors).ToBeHiddenAsync();
        await Expect(dialog.CreateButton).ToBeEnabledAsync();

        await dialog.CancelAsync();
        await annotations.FilterAsync($"key:{PlatformData.Context}.{subject}");
        await Expect(annotations.NoMatchingAnnotations).ToBeVisibleAsync();
    }

    // ANN-NEW-04: New annotation over a deleted key: Cancel, Restore and update, Create new.
    // Create of a deleted key asks "Deleted annotations" (its title, who deleted it) after a lookup that writes nothing:
    // Cancel keeps the dialog and sends no write. "Restore and update" brings the key back with its saved title and
    // its history (deleted, restored); "Create new" over another deleted key starts it empty, as a new lifetime of the
    // key in its history.
    [Test]
    [TestCase(73)]
    [UserRole(Role.Editor)]
    public async Task NewAnnotation_DeletedKey_RestoresOrRecreates()
    {
        var restored = PlatformData.AnnotationKey(PlatformData.Subject);
        var recreated = PlatformData.AnnotationKey(PlatformData.Subject);
        var title = PlatformData.Title();
        await InstallRequestRecorderAsync();
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        var user = await App.SignedInNameAsync();
        await annotations.BulkCreateAsync(restored, recreated);

        var detail = new AnnotationDetail(App, Routes);
        var history = new AnnotationHistory(App);
        await detail.OpenAsync(restored);
        await Expect(detail.Identity).ToHaveTextAsync(restored);
        await detail.Title.FillAsync(title);
        await detail.SaveFormAsync();
        await detail.DeleteAsync(restored);
        await detail.OpenAsync(recreated);
        await Expect(detail.Identity).ToHaveTextAsync(recreated);
        await detail.DeleteAsync(recreated);

        await ClearRecordedRequestsAsync();
        var dialog = await annotations.OpenNewAnnotationAsync();
        await dialog.SelectTypeAsync("Subject");
        await dialog.FillPartAsync("Name", restored.Split('.')[1]);
        var deleted = await dialog.CreateOverDeletedAsync();
        var item = deleted.Item(restored);
        await Expect(item).ToContainTextAsync(title);
        await Expect(item).ToContainTextAsync(new Regex($@"Deleted .+ by\s+{Regex.Escape(user)}"));
        await Expect(deleted.CancelButton).ToBeVisibleAsync();
        await Expect(deleted.CreateNewButton).ToBeVisibleAsync();
        await Expect(deleted.Button(DeletedKeysDialog.RestoreAndUpdateLabel)).ToBeVisibleAsync();

        await deleted.CancelAsync();
        await Expect(dialog.Root).ToBeVisibleAsync();
        await Expect(dialog.KeyPreview).ToHaveTextAsync(restored);
        await ExpectRequestAsync(IsDeletedKeyLookup, "the lookup of deleted keys (POST …/annotations/deleted/lookup)");
        await ExpectNoRequestAsync(request => IsWriteOf(restored)(request) || IsDeletedKeyRestore(request), $"a write of {restored} or a restore (…/annotations/deleted/restore) after Cancel");

        deleted = await dialog.CreateOverDeletedAsync();
        await deleted.RestoreAsync(DeletedKeysDialog.RestoreAndUpdateLabel);
        await Expect(App.Toast($"{restored} restored and updated")).ToBeVisibleAsync();
        await Expect(dialog.Root).ToBeHiddenAsync();
        await App.ExpectAtAsync(Routes.Annotation(restored));
        await Expect(detail.Title).ToHaveValueAsync(title);

        await detail.OpenHistoryTabAsync(restored);
        await history.WaitForEntryAsync(Routes.AnnotationHistory(restored), "Restored");
        await Expect(detail.History).ToContainTextAsync("Deleted");
        await Expect(detail.History).ToContainTextAsync("Restored");

        await detail.CloseAsync();
        dialog = await annotations.OpenNewAnnotationAsync();
        await dialog.SelectTypeAsync("Subject");
        await dialog.FillPartAsync("Name", recreated.Split('.')[1]);
        deleted = await dialog.CreateOverDeletedAsync();
        await deleted.CreateNewAsync();
        await Expect(App.Toast($"{recreated} created")).ToBeVisibleAsync();
        await Expect(dialog.Root).ToBeHiddenAsync();
        await App.ExpectAtAsync(Routes.Annotation(recreated));
        await Expect(detail.Identity).ToHaveTextAsync(recreated);
        await Expect(detail.Title).ToHaveValueAsync(string.Empty);

        await detail.OpenHistoryTabAsync(recreated);
        await history.WaitForEntryAsync(Routes.AnnotationHistory(recreated), "Re-created");
        await Expect(detail.History).ToContainTextAsync(new Regex(@"New lifetime\s*Re-created"));
    }
}
