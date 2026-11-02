using System.Globalization;
using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Platform.UI.Admin.AutomationTests.Support;
using _42.Testing.UI.Automation;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Modeling;

// Templates: the Templates list and the template editor (create, JSON Patch saves with their change count, versions,
// the typed delete by the type's code) and the conflict dialog after a concurrent change. A template changes the
// effective document of every configuration of its type in the view, so the tests work in a disposable project only.
[Area(Area.Modeling)]
public sealed class ModelingTemplateTests : PlatformTest
{
    // The template the conflict test saves from a second page on a deployed target (another writer), opened once.
    private ModelingTemplatePage? otherWriter;

    // MOD-TPL-01: Create, edit, version and delete a template.
    // A new project lists the seven types; Responsibility has no template and no configuration. Create template saves
    // a document as v1, a changed value counts as one change and saves as v2, and the Versions compare v2 with v1 in one
    // changed line. The typed delete asks for the type's code; the detail then shows "No responsibility template", and
    // the list "No template" again.
    // Admin UI bug (424-152 review): TemplateDetail.vue remove() + CodeEditor.vue echo {} back before the 404, so the
    // editor stays with {} and Create instead of the empty state, and leaving it asks "Discard changes?"; the fixed app
    // shows "No responsibility template". The test accepts both.
    [Test]
    [TestCase(147)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task Templates_CreateEditAndDelete_KeepsVersions()
    {
        var project = await CreateDisposableProjectAsync();
        var template = new ModelingTemplatePage(App, project);
        await template.OpenListAsync();
        await Expect(template.TemplateListRows).ToHaveCountAsync(7);
        var row = template.ListRow(PlatformData.Responsibility);
        await Expect(row).ToContainTextAsync("No template");
        await Expect(row).ToContainTextAsync("0 configurations");

        await row.ClickAsync();
        await App.ExpectAtAsync(project.Template(PlatformData.Responsibility));
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Responsibility template", Exact = true })).ToBeVisibleAsync();
        await Expect(template.NoTemplate).ToHaveTextAsync("No responsibility template");
        await Expect(template.NoTemplateDescription).ToBeVisibleAsync();
        await Expect(template.CreateTemplateButton).ToBeVisibleAsync();

        await template.CreateAsync(RetriesTemplate(2));
        await Expect(template.VersionBadge(1)).ToBeVisibleAsync();
        await Expect(template.DeleteButton).ToBeVisibleAsync();

        await template.SetDocumentAsync(RetriesTemplate(3));
        await Expect(template.Changes).ToHaveTextAsync("1 change(s)");
        await Expect(template.SaveButton).ToBeEnabledAsync();

        await template.SaveAsync(2);
        await Expect(template.VersionBadge(2)).ToBeVisibleAsync();

        var versions = template.Versions;
        await template.VersionsTab.ClickAsync();
        await App.ExpectQueryAsync("tab", "versions");
        await versions.WaitForTimelineAsync();
        await Expect(versions.Rows).ToHaveCountAsync(2);
        await Expect(versions.Rows.Nth(0)).ToContainTextAsync("v2");
        await Expect(versions.CurrentBadge(2)).ToBeVisibleAsync();
        await Expect(versions.Rows.Nth(1)).ToContainTextAsync("v1");
        await versions.CompareWithPreviousAsync(2);
        await App.ExpectQueryAsync("from", "1");
        await Expect(versions.CompareHeader(1, 2)).ToBeVisibleAsync();
        await Expect(versions.DiffSummary).ToHaveTextAsync("+1 −1 · 1 change");

        // Delete is in the save bar, under the toast of the second save on a phone.
        await template.EditTab.ClickAsync();
        await App.ExpectQueryAsync("tab", null);
        await Expect(App.Toast("Template saved (v2)")).ToBeHiddenAsync();
        await template.DeleteButton.ClickAsync();
        var delete = template.DeleteDialog("responsibility");
        await delete.ExpectOpenAsync();
        await Expect(delete.Description).ToHaveTextAsync("Configurations of this type stop inheriting its values.");
        await Expect(delete.ConfirmationInput).ToHaveAccessibleNameAsync($"Type {PlatformData.Responsibility} to confirm");
        await Expect(delete.Button("Delete")).ToBeDisabledAsync();

        await delete.TypeConfirmationAsync(PlatformData.Responsibility);
        await delete.ConfirmAsync("Delete");
        await Expect(App.Toast("Template deleted")).ToBeVisibleAsync();

        // Until the template's 404 arrives the editor still shows Save; then either the empty state (the case) or the
        // editor's Create.
        await Expect(template.NoTemplate.Or(template.CreateButton)).ToBeVisibleAsync();
        await Expect(template.DeleteButton).ToHaveCountAsync(0);
        if (await template.NoTemplate.IsVisibleAsync())
        {
            await Expect(template.NoTemplate).ToHaveTextAsync("No responsibility template");
            await Expect(template.CreateTemplateButton).ToBeVisibleAsync();
            await template.Panel.CloseAsync();
        }
        else
        {
            // Admin UI bug (424-152 review): TemplateDetail.vue remove() + CodeEditor.vue echo {} back before the 404,
            // so the editor keeps {} with Create, a change of its own; the fixed app shows "No responsibility template".
            await template.Document.ExpectTextAsync("{}");
            await template.Panel.LeaveButton.ClickAsync();
            await new DiscardChangesDialog(App).DiscardIfAskedAsync();
            await Expect(template.Panel.Root).ToBeHiddenAsync();
        }

        await App.ExpectAtAsync(project.ModelingTab("templates"));
        await Expect(template.ListRow(PlatformData.Responsibility)).ToContainTextAsync("No template");
    }

    // MOD-TPL-02: A concurrent template change opens the conflict dialog.
    // While this editor has an unsaved change of the Responsibility template (v1), another writer saves the template
    // (v2): the save's JSON Patch tests the old value, so the conflict dialog compares the stored template with the draft.
    // "Reload, discard mine" takes the stored one. After a second concurrent save (v3), "Copy mine" copies the draft and
    // "Overwrite" saves it as v4. The other writer is an in-page PUT on the mock (a second tab has a mock database of
    // its own) and a second page of the same sign-in on a deployed target.
    [Test]
    [TestCase(149)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task Templates_ConcurrentChange_OpensConflictDialog()
    {
        await InstallClipboardRecorderAsync();
        var project = await CreateDisposableProjectAsync();
        var template = new ModelingTemplatePage(App, project);
        await template.OpenAsync(PlatformData.Responsibility);
        await template.CreateAsync(RetriesDocument(2));

        await template.SetDocumentAsync(RetriesDocument(5));
        await Expect(template.Changes).ToHaveTextAsync("1 change(s)");

        await SaveTemplateElsewhereAsync(project, retries: 9, expectedVersion: 2);
        await Expect(template.Changes).ToHaveTextAsync("1 change(s)");

        var conflict = new ConfigurationConflictDialog(App);
        await Expect(template.SaveButton).ToBeEnabledAsync();
        await template.Document.PressSaveAsync();
        await conflict.ExpectOpenAsync();
        await conflict.ShowSplitDiffAsync();
        await Expect(conflict.TheirsLabel).ToBeVisibleAsync();
        await Expect(conflict.YoursLabel).ToBeVisibleAsync();
        await Expect(conflict.CopyMineButton).ToBeVisibleAsync();
        await Expect(conflict.ReloadButton).ToBeVisibleAsync();
        await Expect(conflict.OverwriteButton).ToBeVisibleAsync();

        await conflict.ReloadAsync();
        await Expect(template.Document.Content).ToContainTextAsync(Retries(9));
        await Expect(template.SaveButton).ToBeDisabledAsync();

        await template.SetDocumentAsync(RetriesDocument(5));
        await SaveTemplateElsewhereAsync(project, retries: 7, expectedVersion: 3);
        await Expect(template.SaveButton).ToBeEnabledAsync();
        await template.Document.PressSaveAsync();
        await conflict.ExpectOpenAsync();

        await conflict.CopyMineButton.ClickAsync();
        await Expect(conflict.CopiedButton).ToBeVisibleAsync();
        await WaitForCopiedTextAsync("{\n  \"retries\": 5\n}");

        await conflict.OverwriteAsync();
        await Expect(App.Toast("Template saved (v4)")).ToBeVisibleAsync();
        await Expect(template.Document.Content).ToContainTextAsync(Retries(5));
    }

    // {"owner":"<run slug>","retries":<n>}: a template the run owns.
    private string RetriesTemplate(int retries)
    {
        return $$"""{"owner":"{{PlatformData.RunSlug}}","retries":{{Number(retries)}}}""";
    }

    private static string RetriesDocument(int retries)
    {
        return $$"""{"retries":{{Number(retries)}}}""";
    }

    // "retries": <n> as the editor shows it, compact or indented.
    private static Regex Retries(int retries)
    {
        return new Regex($"\"retries\":\\s*{Number(retries)}\\b");
    }

    private static string Number(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    // Another writer saves the Responsibility template of the project as {"retries": <n>}, the expected version: on the
    // mock a PUT of the whole template through the page's fetch (the mock serves it without the editor's JSON Patch
    // tests), on a deployed target the editor of a second page, which saves its own change as a patch.
    private async Task SaveTemplateElsewhereAsync(AppRoutes project, int retries, int expectedVersion)
    {
        if (Target.IsMock)
        {
            var answer = await new MockApi(App, project).FetchAsync(
                "PUT",
                $"v1/{project.Organization}/{project.Project}/{project.View}/template/{PlatformData.Responsibility}",
                new Dictionary<string, int> { ["retries"] = retries });
            await Assert.That(answer.Status).IsEqualTo(200);
            await Assert.That(answer.Json()?["Version"]?.GetValue<int>()).IsEqualTo(expectedVersion);
            return;
        }

        if (otherWriter is null)
        {
            otherWriter = new ModelingTemplatePage(await OpenSecondPageAsync(), project);
            await otherWriter.OpenAsync(PlatformData.Responsibility);
        }

        await otherWriter.SetDocumentAsync(RetriesDocument(retries));
        await otherWriter.SaveAsync(expectedVersion);
    }
}
