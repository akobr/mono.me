using System.Globalization;
using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Modeling;

// Schemas: the Schemas list with its three sections, the New schema dialog, and the schema editor (create, the
// meta-schema and syntax checks, the form preview, a breaking change with Save anyway, Revert, versions and the typed
// delete). Annotation and descendant-type schemas are written on the test's own annotations in the target view; type
// schemas only in a disposable project.
[Area(Area.Modeling)]
public sealed partial class ModelingSchemaTests : PlatformTest
{
    // An object schema with one titled string property, which the form preview renders as the field "Ticket".
    private const string TicketSchema = """{"type":"object","properties":{"ticket":{"type":"string","title":"Ticket"}}}""";

    // MOD-SCH-01: Create an annotation schema for a new annotation.
    // The schema of a new responsibility does not exist yet: its editor names the annotation, applies to none of the
    // view's configurations and offers Create, not Delete. A valid document saves as v1 with Ctrl+S, after which the
    // schema can be deleted, and the list's "Annotation schemas" links it with v1.
    [Test]
    [Elementary]
    [TestCase(92)]
    [UserRole(Role.Editor)]
    public async Task Schemas_CreateAnnotationSchema_ListsVersionOne()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(key);

        var schema = new ModelingSchemaPage(App, Routes);
        await schema.OpenAnnotationAsync(key);
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = $"Schema of {key}", Exact = true })).ToBeVisibleAsync();
        await Expect(schema.Panel.Root.GetByText("Validates the configuration of this annotation, on top of its type schema.", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(schema.AppliesTo).ToHaveTextAsync(ModelingSchemaPage.AppliesToConfigurations(0));
        await Expect(schema.CreateButton).ToBeVisibleAsync();
        await Expect(schema.DeleteButton).ToHaveCountAsync(0);

        await schema.SetDocumentAsync(TicketSchema);
        await Expect(schema.DocumentIssues).ToHaveCountAsync(0);
        await Expect(schema.CreateButton).ToBeEnabledAsync();

        await schema.CreateWithCurrentDocumentAsync();
        await Expect(schema.SaveButton).ToBeDisabledAsync();
        await Expect(schema.DeleteButton).ToBeVisibleAsync();

        await schema.Panel.CloseAsync();
        await App.ExpectAtAsync(Routes.ModelingTab("schemas"));
        await Expect(schema.SchemaListSection("Annotation schemas")).ToBeVisibleAsync();

        var row = schema.AnnotationSchemaRow(key);
        await Expect(row.GetByRole(AriaRole.Link, new() { Name = key, Exact = true })).ToHaveAttributeAsync("href", AppRoutes.HrefEndingWith(Routes.AnnotationSchema(key)));
        await Expect(row).ToContainTextAsync("v1");
    }

    // MOD-SCH-02: New schema offers the kinds and only the types that can descend, then opens the editor.
    // The dialog starts with a type schema of Execution and closes on Cancel without navigating. A descendant-type
    // schema needs a picked key: below a responsibility four types can descend, below a unit of execution none, which
    // disables the type and Open editor. Open editor opens the editor of Execution below the responsibility, whose
    // starter document saves as v1 and is listed under "Descendant-type schemas".
    [Test]
    [TestCase(93)]
    [UserRole(Role.Editor)]
    public async Task Schemas_NewSchemaDialog_OpensDescendantTypeEditor()
    {
        var unitOfExecution = PlatformData.AnnotationKey(PlatformData.UnitOfExecution);
        var responsibility = PlatformData.AncestorKey(unitOfExecution, PlatformData.Responsibility);
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(unitOfExecution);

        var schema = new ModelingSchemaPage(App, Routes);
        await schema.OpenSchemaListAsync();
        var dialog = new NewSchemaDialog(App);
        await dialog.OpenAsync();
        await Expect(dialog.TypeSchemaKind).ToBeCheckedAsync();
        await ExpectTypeLabelAsync(dialog.TypeSelect, "Execution");
        await Expect(dialog.OpenEditorButton).ToBeEnabledAsync();

        await dialog.CancelAsync();
        await App.ExpectAtAsync(Routes.ModelingTab("schemas"));
        await dialog.OpenAsync();

        await dialog.ChooseKindAsync("Descendant-type schema");
        await Expect(dialog.KeyInput).ToBeVisibleAsync();
        await Expect(dialog.KeyInput).ToHaveAttributeAsync("placeholder", NewSchemaDialog.KeyPlaceholder);
        await Expect(dialog.OpenEditorButton).ToBeDisabledAsync();

        await dialog.PickKeyAsync(responsibility);
        var types = await dialog.OpenTypeSelectAsync();
        await Expect(types).ToHaveCountAsync(4);
        if (Target.IsMock)
        {
            await Expect(types).ToHaveTextAsync(["Unit", "Usage", "Execution", "Unit of execution"]);
        }

        await dialog.CloseTypeSelectAsync();
        await ExpectTypeLabelAsync(dialog.TypeSelect, "Execution");
        await Expect(dialog.OpenEditorButton).ToBeEnabledAsync();

        await dialog.PickKeyAsync(unitOfExecution);
        await Expect(dialog.NothingDescends(unitOfExecution)).ToBeVisibleAsync();
        await Expect(dialog.TypeSelect).ToBeDisabledAsync();
        await Expect(dialog.OpenEditorButton).ToBeDisabledAsync();

        // The key of the unit of execution left no type; with the responsibility again the select takes the first type
        // that can descend (Unit), so Execution, the third, is picked again.
        await dialog.PickKeyAsync(responsibility);
        await ExpectTypeLabelAsync(dialog.TypeSelect, "Unit");
        await dialog.OpenTypeSelectAsync();
        await dialog.PickTypeAtAsync(2);
        await ExpectTypeLabelAsync(dialog.TypeSelect, "Execution");
        await dialog.OpenEditorAsync();
        await App.ExpectAtAsync(Routes.DescendantSchema(responsibility, PlatformData.Execution));
        await Expect(schema.Document.Content).ToBeVisibleAsync();
        if (Target.IsMock)
        {
            await Expect(schema.Panel.Root).ToHaveAccessibleNameAsync($"Execution schema below {responsibility}");
        }

        await schema.CreateWithCurrentDocumentAsync();

        await schema.Panel.CloseAsync();
        await App.ExpectAtAsync(Routes.ModelingTab("schemas"));
        var row = schema.DescendantSchemaRow(responsibility, PlatformData.Execution);
        await Expect(row).ToContainTextAsync("v1");
        await Expect(row.GetByRole(AriaRole.Link, new() { Name = responsibility, Exact = true })).ToHaveAttributeAsync("href", AppRoutes.HrefEndingWith(Routes.Annotation(responsibility)));
        var below = row.Locator($"a[href$=\"{Routes.DescendantSchema(responsibility, PlatformData.Execution)}\"]");
        await Expect(below).ToBeVisibleAsync();
        if (Target.IsMock)
        {
            await Expect(below).ToHaveTextAsync("Execution below");
        }
    }

    // MOD-SCH-03: Create and delete a type schema.
    // A new project has no schemas: seven type rows, Responsibility without a schema, and two empty sections (on a
    // deployed target; the mock gives every view its base documents). The Responsibility schema starts with the starter
    // document and Create; saved as v1 its row shows v1. The typed delete ("delete") removes it, and the editor offers
    // Create with the starter document again, unchanged, so closing asks nothing; the row shows "No schema" again. A
    // type schema validates every configuration of the type in the view, hence the disposable project.
    [Test]
    [TestCase(96)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task Schemas_CreateAndDeleteTypeSchema_UpdatesTypeRow()
    {
        var project = await CreateDisposableProjectAsync();
        var schema = new ModelingSchemaPage(App, project);
        await schema.OpenSchemaListAsync();
        await Expect(schema.SchemaListRows("Type schemas")).ToHaveCountAsync(7);
        await Expect(schema.TypeSchemaRow(PlatformData.Responsibility)).ToContainTextAsync("No schema");
        if (!Target.IsMock)
        {
            // The mock gives every view its base documents (the schemas of cnt, exe and uxe, one annotation schema and
            // one descendant-type schema), also a new project's.
            await Expect(schema.NoSchemasIn("Annotation schemas")).ToBeVisibleAsync();
            await Expect(schema.NoSchemasIn("Descendant-type schemas")).ToBeVisibleAsync();
        }

        await schema.TypeSchemaLink(PlatformData.Responsibility).ClickAsync();
        await App.ExpectAtAsync(project.TypeSchema(PlatformData.Responsibility));
        await Expect(Page.GetByRole(AriaRole.Region, new() { Name = "Responsibility schema", Exact = true })).ToBeVisibleAsync();
        await Expect(schema.Panel.Root.GetByText("Validates every responsibility configuration of the view.", new() { Exact = true })).ToBeVisibleAsync();
        await schema.Document.ExpectTextAsync(ModelingSchemaPage.StarterDocument);
        await Expect(schema.CreateButton).ToBeVisibleAsync();

        await schema.CreateAsync("""{"type":"object","properties":{"enabled":{"type":"boolean"}}}""");
        await Expect(schema.DeleteButton).ToBeVisibleAsync();

        await schema.Panel.CloseAsync();
        await Expect(schema.TypeSchemaRow(PlatformData.Responsibility)).ToContainTextAsync("v1");
        await Expect(schema.TypeSchemaRow(PlatformData.Responsibility)).Not.ToContainTextAsync("No schema");

        // The toast of the save covers the save bar on a phone, and a pointer on it keeps it open.
        await schema.TypeSchemaLink(PlatformData.Responsibility).ClickAsync();
        await Expect(App.Toast("Schema saved (v1)")).ToBeHiddenAsync();
        await schema.DeleteButton.ClickAsync();
        var delete = schema.DeleteDialog("Responsibility schema");
        await delete.ExpectOpenAsync();
        await Expect(delete.Description).ToHaveTextAsync("Configurations are no longer validated by it, and the form disappears where it was the only schema.");
        await Expect(delete.Button("Delete")).ToBeDisabledAsync();

        await Expect(delete.ConfirmationInput).ToHaveAccessibleNameAsync("Type delete to confirm");
        await delete.TypeConfirmationAsync("delete");
        await delete.ConfirmAsync("Delete");
        await Expect(App.Toast("Schema deleted")).ToBeVisibleAsync();

        // Create shows once the schema's 404 arrived (Save until then), and with it the starter document (424-158).
        await Expect(schema.CreateButton).ToBeVisibleAsync();
        await schema.Document.ExpectTextAsync(ModelingSchemaPage.StarterDocument);

        await LeaveDeletedSchemaAsync(schema);
        await Expect(schema.TypeSchemaRow(PlatformData.Responsibility)).ToContainTextAsync("No schema");
    }

    // MOD-SCH-04: Meta-schema and syntax errors block saving a schema, and Revert restores it.
    // The editor checks a document against the JSON Schema meta-schema and its syntax before anything is sent: an
    // unknown type lists its issue and has no form preview, a syntax error shows its line; both disable Create. A valid
    // document saves as v1, and Revert undoes a later change.
    [Test]
    [TestCase(99)]
    [UserRole(Role.Editor)]
    public async Task Schemas_InvalidDocument_BlocksSaveUntilFixed()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(key);

        var schema = new ModelingSchemaPage(App, Routes);
        await schema.OpenAnnotationAsync(key);
        await schema.SetDocumentAsync("""{"type":"objekt"}""");
        await Expect(schema.DocumentIssues).ToContainTextAsync(AnyOfIssue());
        await Expect(schema.CreateButton).ToBeDisabledAsync();
        await schema.ShowPreviewPaneAsync();
        await Expect(schema.NoPreview).ToBeVisibleAsync();
        await schema.ShowSchemaPaneAsync();

        await schema.SetDocumentAsync("""{"type":""");
        await Expect(schema.SyntaxError).ToBeVisibleAsync();
        await Expect(schema.CreateButton).ToBeDisabledAsync();

        await schema.SetDocumentAsync(LimitSchema(10));
        await Expect(schema.SyntaxError).ToHaveCountAsync(0);
        await Expect(schema.DocumentIssues).ToHaveCountAsync(0);
        await schema.CreateWithCurrentDocumentAsync();

        await schema.ReplaceInDocumentAsync("10", "20");
        await Expect(schema.SaveButton).ToBeEnabledAsync();
        await Expect(schema.RevertButton).ToBeEnabledAsync();

        // Revert is beside Save, under the toast of the save.
        await Expect(App.Toast("Schema saved (v1)")).ToBeHiddenAsync();
        await schema.RevertButton.ClickAsync();
        await Expect(schema.Document.Content).ToContainTextAsync(Maximum(10));
        await Expect(schema.SaveButton).ToBeDisabledAsync();
        await Expect(schema.RevertButton).ToBeDisabledAsync();
    }

    // MOD-SCH-05: A schema that breaks a configuration lists it, and Save anyway forces it.
    // A new responsibility with its own configuration {"retries": 5} gets an annotation schema with a maximum of 1: the
    // save is refused with the alert "Existing configurations would not comply", whose table links the configuration
    // with its view and error. Save anyway asks once more and then saves the schema as v1 anyway.
    [Test]
    [TestCase(101)]
    [UserRole(Role.Editor)]
    public async Task Schemas_BreakingChange_SaveAnywayForcesIt()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(key);

        var configuration = new ConfigurationDetail(App, Routes);
        await configuration.OpenAsync(key);
        await configuration.CreateEmptyAsync();
        await Expect(App.Toast("Configuration created")).ToBeVisibleAsync();
        await configuration.SetDocumentAsync("{\"retries\": 5}");
        await configuration.SaveAsync(2);

        var schema = new ModelingSchemaPage(App, Routes);
        await schema.OpenAnnotationAsync(key);
        await Expect(schema.AppliesTo).ToHaveTextAsync(ModelingSchemaPage.AppliesToConfigurations(1));

        await schema.SetDocumentAsync("""{"type":"object","properties":{"retries":{"type":"integer","maximum":1}}}""");
        await Expect(schema.CreateButton).ToBeEnabledAsync();
        await schema.Document.PressSaveAsync();
        await Expect(schema.NonCompliant).ToBeVisibleAsync();
        await Expect(App.Toast(SchemaSavedTitle())).ToHaveCountAsync(0);

        await Expect(schema.NonCompliantTable.GetByRole(AriaRole.Columnheader)).ToHaveTextAsync(["Configuration", "View", "Errors"]);
        var link = schema.NonCompliantTable.GetByRole(AriaRole.Link, new() { Name = key, Exact = true });
        await Expect(link).ToHaveAttributeAsync("href", AppRoutes.HrefEndingWith(Routes.Configuration(key)));
        var row = schema.NonCompliantTable.GetByRole(AriaRole.Row).Filter(new() { Has = Page.GetByRole(AriaRole.Link, new() { Name = key, Exact = true }) });
        await Expect(row.GetByRole(AriaRole.Cell)).ToHaveTextAsync([key, Routes.View, "/retries Number too big"]);

        await schema.SaveAnywayButton.ClickAsync();
        var confirm = new ConfirmDialog(App, "Save although configurations break?");
        await confirm.ExpectOpenAsync();
        await Expect(confirm.Description).ToHaveTextAsync("1 configuration(s) will no longer comply with the schema. They keep working, but their next save without force fails until they are fixed.");

        await confirm.ConfirmAsync("Save anyway");
        await Expect(App.Toast("Schema saved (v1)")).ToBeVisibleAsync();
        await Expect(schema.NonCompliant).ToBeHiddenAsync();
        await Expect(schema.SaveButton).ToBeDisabledAsync();
    }

    // MOD-SCH-06: The form preview renders the schema and keeps sample data.
    // An object schema renders as a form whose field fills the sample data, which Reset sample empties; a schema that is
    // not an object schema has no preview. Revert of a schema that does not exist returns to the starter document, and
    // with nothing saved its Versions say "No schema, no history". Below 1280 px the preview is the tab "Preview".
    [Test]
    [TestCase(103)]
    [UserRole(Role.Editor)]
    public async Task Schemas_FormPreview_RendersObjectSchema()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(key);

        var schema = new ModelingSchemaPage(App, Routes);
        await schema.OpenAnnotationAsync(key);
        await schema.SetDocumentAsync(TicketSchema);
        await schema.ShowPreviewPaneAsync();
        var ticket = schema.PreviewField("Ticket");
        await Expect(ticket).ToBeVisibleAsync();

        await ticket.FillAsync("AT-1");
        await schema.SampleDataSummary.ClickAsync();
        await Expect(schema.SampleData).ToContainTextAsync(TicketSample());

        await schema.ResetSampleButton.ClickAsync();
        await Expect(ticket).ToHaveValueAsync(string.Empty);
        await Expect(schema.SampleData).ToHaveTextAsync("{}");

        await schema.ShowSchemaPaneAsync();
        await schema.SetDocumentAsync("""{"type":"array"}""");
        await schema.ShowPreviewPaneAsync();
        await Expect(schema.NoPreview).ToBeVisibleAsync();
        await Expect(schema.NoPreviewDescription).ToBeVisibleAsync();

        await schema.ShowSchemaPaneAsync();
        await schema.RevertButton.ClickAsync();
        await schema.Document.ExpectTextAsync(ModelingSchemaPage.StarterDocument);
        await schema.VersionsTab.ClickAsync();
        await App.ExpectQueryAsync("tab", "versions");
        await Expect(schema.NoHistory).ToBeVisibleAsync();
    }

    // MOD-SCH-07: Schema versions show the history and a comparison, and a typed delete ends them.
    // An annotation schema saved twice (maximum 5, then 9) lists v2 as current above v1; v1 shows read-only, and v2
    // compared with v1 differs in one line. The typed delete stays disabled until "delete" is typed and confirms with
    // Enter; then nothing is left: no history, and no row in "Annotation schemas". The editor of the deleted schema holds
    // the unchanged starter document, so leaving it asks nothing (LeaveDeletedSchemaAsync).
    [Test]
    [TestCase(105)]
    [UserRole(Role.Editor)]
    public async Task Schemas_VersionsAndDelete_ShowHistoryThenNone()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Responsibility);
        var annotations = new AnnotationsPage(App, Routes);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(key);

        var schema = new ModelingSchemaPage(App, Routes);
        await schema.OpenAnnotationAsync(key);
        await schema.CreateAsync(LimitSchema(5));
        await schema.SetDocumentAsync(LimitSchema(9));
        await schema.SaveAsync(2);

        var versions = schema.Versions;
        await schema.VersionsTab.ClickAsync();
        await App.ExpectQueryAsync("tab", "versions");
        await versions.WaitForTimelineAsync();
        await Expect(versions.Rows).ToHaveCountAsync(2);
        await Expect(versions.Rows.Nth(0)).ToContainTextAsync("v2");
        await Expect(versions.CurrentBadge(2)).ToBeVisibleAsync();
        await Expect(versions.Rows.Nth(1)).ToContainTextAsync("v1");
        await Expect(versions.CurrentBadge(1)).ToHaveCountAsync(0);

        var first = await versions.ViewAsync(1);
        await Expect(schema.Panel.Root.GetByText("Version v1", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(first.Content).ToContainTextAsync(Maximum(5));
        await Expect(first.Content).ToHaveAttributeAsync("contenteditable", "false");
        await Expect(versions.BackToHistoryButton).ToBeVisibleAsync();

        await versions.BackToHistoryAsync();
        await versions.CompareWithPreviousAsync(2);
        await App.ExpectQueryAsync("from", "1");
        await Expect(versions.CompareHeader(1, 2)).ToBeVisibleAsync();
        await Expect(versions.DiffSummary).ToHaveTextAsync("+1 −1 · 1 change");
        await Expect(schema.Panel.Root.Locator(".cm-mergeView, .cm-changedLine").First).ToBeVisibleAsync();

        await schema.EditTab.ClickAsync();
        await App.ExpectQueryAsync("tab", null);
        await Expect(schema.Document.Content).ToContainTextAsync(Maximum(9));

        // The toast of the second save covers the save bar on a phone, and a pointer on it keeps it open.
        await Expect(App.Toast("Schema saved (v2)")).ToBeHiddenAsync();
        var delete = schema.DeleteDialog($"Schema of {key}");
        await schema.DeleteButton.ClickAsync();
        await delete.ExpectOpenAsync();
        await delete.TypeConfirmationAsync("delet");
        await Expect(delete.Button("Delete")).ToBeDisabledAsync();

        await delete.CancelAsync();
        await schema.DeleteButton.ClickAsync();
        await delete.ExpectOpenAsync();
        await delete.TypeConfirmationAsync("delete");
        await Expect(delete.Button("Delete")).ToBeEnabledAsync();
        await delete.ConfirmationInput.PressAsync("Enter");
        await Expect(App.Toast("Schema deleted")).ToBeVisibleAsync();
        await Expect(delete.Root).ToBeHiddenAsync();
        await Expect(schema.CreateButton).ToBeVisibleAsync();

        await schema.VersionsTab.ClickAsync();
        await Expect(schema.NoHistory).ToBeVisibleAsync();

        await LeaveDeletedSchemaAsync(schema);
        await App.ExpectAtAsync(Routes.ModelingTab("schemas"));
        await Expect(schema.SchemaListSection("Annotation schemas")).ToBeVisibleAsync();
        await Expect(schema.AnnotationSchemaRow(key)).ToHaveCountAsync(0);
    }

    // An object schema with an integer "limit" of the maximum.
    private static string LimitSchema(int maximum)
    {
        return """{"type":"object","properties":{"limit":{"type":"integer","maximum":""" + maximum.ToString(CultureInfo.InvariantCulture) + "}}}";
    }

    // "maximum": <n> as the editor shows it, compact or pretty-printed.
    private static Regex Maximum(int maximum)
    {
        return new Regex($"\"maximum\":\\s*{maximum}\\b");
    }

    // A deleted schema's editor holds the starter document, unchanged (424-158): the editor is clean, and leaving
    // returns to the list without "Discard changes?".
    private async Task LeaveDeletedSchemaAsync(ModelingSchemaPage schema)
    {
        await Expect(Page).Not.ToHaveTitleAsync(DiscardChangesDialog.DirtyTitle());
        await schema.Panel.CloseAsync();
        await Expect(new DiscardChangesDialog(App).Root).ToHaveCountAsync(0);
        await Expect(schema.SchemaListSection("Type schemas")).ToBeVisibleAsync();
    }

    // The type's label shows only on the mock, whose labels are the built-in ones; a deployed project may rename it.
    private async Task ExpectTypeLabelAsync(ILocator select, string label)
    {
        if (Target.IsMock)
        {
            await Expect(select).ToHaveTextAsync(label);
        }
    }

    [GeneratedRegex(@"/type\s+must match a schema in anyOf")]
    private static partial Regex AnyOfIssue();

    [GeneratedRegex(@"""ticket"":\s*""AT-1""")]
    private static partial Regex TicketSample();

    [GeneratedRegex("^Schema saved")]
    private static partial Regex SchemaSavedTitle();
}
