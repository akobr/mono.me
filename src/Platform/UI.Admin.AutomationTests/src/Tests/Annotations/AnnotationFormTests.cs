using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Annotations;

// Annotation Form: the Annotation tab of the detail, its fields and their limits, the status they drive, the JSON and
// YAML modes, labels, values, the unit fields and the guard of unsaved changes.
[Area(Area.Annotations)]
public sealed partial class AnnotationFormTests : PlatformTest
{
    // The Markdown of ANN-FRM-01: a heading, a list, raw HTML, a javascript: link and an https link.
    private const string MarkdownDescription = "## Endpoints\n\n- api\n- web\n\n<b>bold</b> [x](javascript:alert(1)) [ok](https://example.com)";

    // ANN-FRM-01: Title and Markdown description save, and the preview is safe.
    // An empty description previews as "Nothing to preview."; a title and a Markdown description mark the form and the
    // document title as unsaved. The preview renders the heading and the list, shows raw HTML as text, drops the
    // javascript: link and opens the https link in a new tab. Ctrl+S saves both, which a fresh load of the detail shows
    // with the update in the stamps.
    [Test]
    [TestCase(181)]
    [UserRole(Role.Editor)]
    public async Task AnnotationForm_TitleAndMarkdown_SaveAndPreviewSafely()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Subject);
        var title = PlatformData.Title();
        var annotations = new AnnotationsPage(App, Routes);
        var detail = new AnnotationDetail(App, Routes);
        var form = new AnnotationForm(App);
        await annotations.OpenAsync();
        var user = await App.SignedInNameAsync();
        await annotations.BulkCreateAsync(key);
        await detail.OpenFormAsync(key);

        await form.DescriptionTab("Preview").ClickAsync();
        await Expect(form.NothingToPreview).ToBeVisibleAsync();

        await form.DescriptionTab("Write").ClickAsync();
        await form.Title.FillAsync(title);
        await form.Description.FillAsync(MarkdownDescription);
        await Expect(form.UnsavedChanges).ToBeVisibleAsync();
        await Expect(Page).ToHaveTitleAsync(DirtyTitle());

        await form.DescriptionTab("Preview").ClickAsync();
        var preview = form.DescriptionPreview;
        await Expect(preview.GetByRole(AriaRole.Heading, new() { Name = "Endpoints", Exact = true })).ToBeVisibleAsync();
        await Expect(preview.GetByRole(AriaRole.Listitem)).ToHaveTextAsync(["api", "web"]);
        await Expect(preview.GetByText("<b>bold</b>")).ToBeVisibleAsync();
        await Expect(preview.Locator("b")).ToHaveCountAsync(0);
        await Expect(preview.GetByRole(AriaRole.Link, new() { Name = "x", Exact = true })).ToHaveCountAsync(0);
        var ok = preview.GetByRole(AriaRole.Link, new() { Name = "ok", Exact = true });
        await Expect(ok).ToHaveAttributeAsync("href", "https://example.com");
        await Expect(ok).ToHaveAttributeAsync("target", "_blank");
        await Expect(ok).ToHaveAttributeAsync("rel", "noopener noreferrer");

        await form.SaveAsync();
        await Expect(form.UnsavedChanges).ToBeHiddenAsync();
        await Expect(Page).Not.ToHaveTitleAsync(DirtyTitle());

        await detail.OpenFormAsync(key);
        await Expect(form.Title).ToHaveValueAsync(title);
        await Expect(form.Description).ToHaveValueAsync(MarkdownDescription);
        await Expect(detail.StampsLink).ToHaveTextAsync(new Regex($@"·\s*Updated .+ by {Regex.Escape(user)}$"));
    }

    // ANN-FRM-02: Field limits show their messages and block Save.
    // A title over 200 characters gets its message and Save sends nothing; a relative documentation link gets its own
    // until it is an absolute https URL, which offers "Open the link"; a description over 10,000 characters gets the
    // count hint and its message. Within the limits the form saves, and a fresh load shows the saved title and link.
    [Test]
    [TestCase(198)]
    [UserRole(Role.Editor)]
    public async Task AnnotationForm_FieldLimits_BlockSave()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Subject);
        var name = key.Split('.')[1];
        var title = PlatformData.Text(200);
        var link = $"https://example.com/{name}";
        await InstallRequestRecorderAsync();
        var annotations = new AnnotationsPage(App, Routes);
        var detail = new AnnotationDetail(App, Routes);
        var form = new AnnotationForm(App);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(key);
        await detail.OpenFormAsync(key);

        await form.Title.FillAsync(PlatformData.Text(201));
        await Expect(form.FieldMessage("At most 200 characters.")).ToBeVisibleAsync();

        await ClearRecordedRequestsAsync();
        await form.SaveButton.ClickAsync();
        await Expect(form.FieldMessage("At most 200 characters.")).ToBeVisibleAsync();
        await Expect(App.Notifications.GetByText("Annotation saved", new() { Exact = true })).ToHaveCountAsync(0);
        await ExpectNoWriteAsync(key);

        await form.Title.FillAsync(title);
        await form.DocumentationLink.FillAsync($"example.com/{name}");
        await Expect(form.FieldMessage("At most 200 characters.")).ToHaveCountAsync(0);
        await Expect(form.FieldMessage("Use an absolute http(s) URL.")).ToBeVisibleAsync();

        await form.DocumentationLink.FillAsync(link);
        await Expect(form.FieldMessage("Use an absolute http(s) URL.")).ToHaveCountAsync(0);
        await Expect(form.OpenTheLinkButton).ToBeVisibleAsync();

        await form.Description.FillAsync(PlatformData.Text(10_001));
        await Expect(form.FieldMessage("10,001 / 10,000")).ToBeVisibleAsync();
        await Expect(form.FieldMessage("At most 10,000 characters.")).ToBeVisibleAsync();

        await form.Description.FillAsync(PlatformData.Text(9_000));
        await form.SaveButton.ClickAsync();
        await Expect(App.Toast("Annotation saved")).ToBeVisibleAsync();
        await Expect(form.Panel.Root.GetByText(DescriptionCount())).ToHaveCountAsync(0);
        await Expect(form.FieldMessage("At most 10,000 characters.")).ToHaveCountAsync(0);

        await detail.OpenFormAsync(key);
        await Expect(form.Title).ToHaveValueAsync(title);
        await Expect(form.DocumentationLink).ToHaveValueAsync(link);
    }

    // ANN-FRM-03: Disabled switch and validity dates drive the status.
    // The status follows what is saved: Disabled from the switch, Scheduled from a ValidFrom in the future and Expired
    // from an ExpiresAt in the past (both set in the JSON mode). A ValidFrom after ExpiresAt is refused under the editor
    // and nothing is written; after Revert, clearing Expires in the form makes the subject active again, which the
    // status filters of the list confirm.
    [Test]
    [TestCase(200)]
    [UserRole(Role.Editor)]
    public async Task AnnotationForm_DisabledAndDates_DriveStatus()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Subject);
        await InstallRequestRecorderAsync();
        var annotations = new AnnotationsPage(App, Routes);
        var detail = new AnnotationDetail(App, Routes);
        var form = new AnnotationForm(App);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(key);
        await detail.OpenFormAsync(key);

        await form.DisabledSwitch.ClickAsync();
        await Expect(form.DisabledSwitch).ToBeCheckedAsync();
        await form.SaveButton.ClickAsync();
        await Expect(App.Toast("Annotation saved")).ToBeVisibleAsync();
        await Expect(detail.StatusBadge).ToHaveTextAsync("Disabled");

        await form.SelectModeAsync("JSON");
        await form.EditDocumentAsync(document =>
        {
            document["IsDisabled"] = false;
            document["ValidFrom"] = PlatformData.AnnotationInstant(10);
        });
        await form.SaveAsync();
        await Expect(detail.StatusBadge).ToHaveTextAsync("Scheduled");

        await form.EditDocumentAsync(document =>
        {
            document["ValidFrom"] = null;
            document["ExpiresAt"] = PlatformData.AnnotationInstant(-1);
        });
        await form.SaveAsync();
        await Expect(detail.StatusBadge).ToHaveTextAsync("Expired");

        await form.EditDocumentAsync(document => document["ValidFrom"] = PlatformData.AnnotationInstant(0));
        await ClearRecordedRequestsAsync();
        await form.SaveButton.ClickAsync();
        await Expect(form.DocumentAlerts.Filter(new() { HasText = "Expires must be after Valid from." })).ToHaveTextAsync("ExpiresAt Expires must be after Valid from.");
        await Expect(detail.StatusBadge).ToHaveTextAsync("Expired");
        await ExpectNoWriteAsync(key);

        await form.RevertButton.ClickAsync();
        await form.SelectModeAsync("Form");
        await form.ExpiresClearButton.ClickAsync();
        await Expect(form.ExpiresClearButton).ToHaveCountAsync(0);
        await form.SaveButton.ClickAsync();
        await Expect(App.Toast("Annotation saved")).ToBeVisibleAsync();
        await Expect(form.ExpiresDate.GetByRole(AriaRole.Spinbutton, new() { NameRegex = YearSegment() })).ToHaveTextAsync("yyyy");
        await Expect(detail.StatusBadge).ToHaveTextAsync("Active");

        await detail.CloseAsync();
        await annotations.FilterAsync($"key:{key} is:active");
        await Expect(annotations.Item(key)).ToBeVisibleAsync();
        await annotations.FilterAsync($"key:{key} is:expired");
        await Expect(annotations.NoMatchingAnnotations).ToBeVisibleAsync();
    }

    // ANN-FRM-04: JSON and YAML modes edit the annotation and guard the key fields.
    // The JSON mode (?format=json) edits the whole annotation and saves a title. A changed AnnotationKey is refused
    // under the editor, disables Save and keeps the mode until it is fixed; after Revert the YAML mode (?format=yaml)
    // shows the saved title, and Ctrl+Shift+F cycles back to the form, which the URL no longer names.
    [Test]
    [TestCase(202)]
    [UserRole(Role.Editor)]
    public async Task AnnotationForm_JsonAndYaml_EditAndGuardKeyFields()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Subject);
        var title = PlatformData.Title();
        var annotations = new AnnotationsPage(App, Routes);
        var detail = new AnnotationDetail(App, Routes);
        var form = new AnnotationForm(App);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(key);
        await detail.OpenFormAsync(key);

        await form.SelectModeAsync("JSON");
        await App.ExpectQueryAsync("format", "json");
        await Expect(form.Document.Content).ToContainTextAsync($"\"AnnotationKey\": \"{key}\"");

        await form.EditDocumentAsync(document => document["Title"] = title);
        await form.SaveButton.ClickAsync();
        await Expect(App.Toast("Annotation saved")).ToBeVisibleAsync();

        await form.EditDocumentAsync(document => document["AnnotationKey"] = $"{key}-changed");
        await Expect(form.DocumentAlerts.Filter(new() { HasText = "is part of the key" })).ToHaveTextAsync("AnnotationKey is part of the key and cannot change.");
        await Expect(form.SaveButton).ToBeDisabledAsync();

        await form.Mode("Form").ClickAsync();
        await Expect(App.Toast("Fix the syntax error first")).ToBeVisibleAsync();
        await Expect(form.Mode("JSON")).ToHaveAttributeAsync("aria-checked", "true");
        await App.ExpectQueryAsync("format", "json");

        await form.RevertButton.ClickAsync();
        await Expect(form.DocumentAlerts.Filter(new() { HasText = "is part of the key" })).ToHaveCountAsync(0);
        await form.SelectModeAsync("YAML");
        await App.ExpectQueryAsync("format", "yaml");
        var yaml = await form.Document.TextAsync();
        if (!yaml.Contains("Title: ", StringComparison.Ordinal) || !yaml.Contains(title, StringComparison.Ordinal))
        {
            Assert.Fail($"The YAML document should hold 'Title: ' and the title '{title}'; it is:\n{yaml}");
        }

        await form.Document.Content.PressAsync("ControlOrMeta+Shift+F");
        await Expect(form.Mode("Form")).ToHaveAttributeAsync("aria-checked", "true");
        await App.ExpectQueryAsync("format", null);
        await Expect(form.Title).ToHaveValueAsync(title);
    }

    // ANN-FRM-05: Labels: create one, reuse it on a second annotation and filter by it.
    // A new label is created in the first subject's Labels and saved, so the label: filter lists it; in the second
    // subject the same label is a suggestion with its count of 1 and no Create option, and once it is saved there the
    // filter lists both.
    [Test]
    [TestCase(205)]
    [UserRole(Role.Editor)]
    public async Task AnnotationForm_Labels_CreateReuseAndFilter()
    {
        var first = PlatformData.AnnotationKey(PlatformData.Subject);
        var second = PlatformData.AnnotationKey(PlatformData.Subject);
        var label = PlatformData.LabelName();
        var annotations = new AnnotationsPage(App, Routes);
        var detail = new AnnotationDetail(App, Routes);
        var form = new AnnotationForm(App);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(first, second);
        await annotations.WaitForCatalogAsync();

        await annotations.FilterAsync($"key:{first}");
        await annotations.OpenAnnotationFromListAsync(first);
        await Expect(form.Panel.Root.GetByTestId("annotation-identity")).ToHaveTextAsync(first);
        await form.AddLabelAsync(label, existing: false);
        await form.SaveAsync();
        await Expect(form.LabelTag(label)).ToBeVisibleAsync();

        await detail.CloseAsync();
        await annotations.FilterAsync($"label:{label}");
        await Expect(annotations.FilterCount).ToHaveTextAsync(CountOf(1));
        await Expect(annotations.Item(first)).ToBeVisibleAsync();
        if (Page.ViewportSize is not { Width: < AnnotationsPage.GridWidth })
        {
            await Expect(await annotations.GridCellAsync(first, "Labels")).ToContainTextAsync(label);
        }

        await annotations.FilterAsync($"key:{second}");
        await annotations.OpenAnnotationFromListAsync(second);
        await Expect(form.Panel.Root.GetByTestId("annotation-identity")).ToHaveTextAsync(second);
        await form.Labels.FillAsync(label);
        await Expect(form.LabelOption(label, 1)).ToBeVisibleAsync();
        await Expect(form.CreateLabelOption(label)).ToHaveCountAsync(0);

        await form.LabelOption(label, 1).ClickAsync();
        await Expect(form.LabelTag(label)).ToBeVisibleAsync();
        await form.Panel.Root.GetByTestId("annotation-identity").ClickAsync();
        await form.SaveAsync();

        await detail.CloseAsync();
        await annotations.FilterAsync($"label:{label}");
        await Expect(annotations.FilterCount).ToHaveTextAsync(CountOf(2));
    }

    // ANN-FRM-06: Values editor: typed rows, duplicate keys and Edit all as JSON.
    // Values starts empty. A string row and a number row are added with their keys, types and values; a third row with
    // a key that is taken is reported as a duplicate until it is removed, and the two rows save. "Edit all as JSON/YAML"
    // shows the saved map with its types, and has:values lists the subject.
    [Test]
    [TestCase(207)]
    [UserRole(Role.Editor)]
    public async Task AnnotationForm_Values_TypedRowsAndDuplicates()
    {
        var key = PlatformData.AnnotationKey(PlatformData.Subject);
        var annotations = new AnnotationsPage(App, Routes);
        var detail = new AnnotationDetail(App, Routes);
        var form = new AnnotationForm(App);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(key);
        await detail.OpenFormAsync(key);
        await Expect(form.NoValues).ToBeVisibleAsync();

        await form.AddValueButton.ClickAsync();
        await form.ValueKey(0).FillAsync("region");
        await form.Value(0).FillAsync("eu");
        await Expect(form.ValueKey(0)).ToHaveValueAsync("region");
        await Expect(form.ValueType(0)).ToHaveTextAsync("string");
        await Expect(form.Value(0)).ToHaveValueAsync("eu");

        await form.AddValueButton.ClickAsync();
        await form.ValueKey(1).FillAsync("retries");
        await form.SetValueTypeAsync(1, "number");
        await form.Value(1).FillAsync("3");
        await form.Value(1).BlurAsync();
        await Expect(form.ValueKey(1)).ToHaveValueAsync("retries");
        await Expect(form.ValueType(1)).ToHaveTextAsync("number");
        await Expect(form.Value(1)).ToHaveValueAsync("3");

        await form.AddValueButton.ClickAsync();
        await form.ValueKey(2).FillAsync("retries");
        await Expect(form.DuplicateKeys).ToHaveTextAsync("Duplicate keys: retries");

        await form.RemoveValueButton("retries").Last.ClickAsync();
        await Expect(form.DuplicateKeys).ToHaveCountAsync(0);
        await form.SaveButton.ClickAsync();
        await Expect(App.Toast("Annotation saved")).ToBeVisibleAsync();

        await detail.OpenFormAsync(key);
        await Expect(form.ValuesEditAllLabel).ToBeVisibleAsync();
        await form.ValuesEditAllLabel.ClickAsync();
        await Expect(form.ValuesEditAllSwitch).ToBeCheckedAsync();
        var values = JsonNode.Parse(await TextOfAsync(form.ValuesDocument))?.AsObject();
        if (values?["region"]?.GetValue<string>() != "eu" || values["retries"]?.GetValue<int>() != 3 || values.Count != 2)
        {
            Assert.Fail($"The editor Values should hold {{ \"region\": \"eu\", \"retries\": 3 }}; it holds {values?.ToJsonString()}.");
        }

        await Expect(form.ValuesDocument.Content).ToContainTextAsync("\"region\": \"eu\"");
        await Expect(form.ValuesDocument.Content).ToContainTextAsync("\"retries\": 3");

        await detail.CloseAsync();
        await annotations.FilterAsync($"key:{key} has:values");
        await Expect(annotations.Item(key)).ToBeVisibleAsync();
    }

    // ANN-FRM-07: Unit fields: unit type, definition and the Cron hint.
    // A bulk-created unit starts as UnitType event with the definition unknown. Time explains itself and its Cron
    // definition, a definition without 5 or 6 fields gets the hint (which never blocks), an empty one its message, and
    // a Cron definition saves, which the JSON mode shows. A responsibility has no Unit fields.
    [Test]
    [TestCase(218)]
    [UserRole(Role.Editor)]
    public async Task AnnotationForm_UnitFields_TypeDefinitionAndCronHint()
    {
        var unit = PlatformData.AnnotationKey(PlatformData.Unit);
        var responsibility = PlatformData.AncestorKey(unit, PlatformData.Responsibility);
        var annotations = new AnnotationsPage(App, Routes);
        var detail = new AnnotationDetail(App, Routes);
        var form = new AnnotationForm(App);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync(unit);
        await detail.OpenFormAsync(unit);

        await Expect(form.UnitFields).ToBeVisibleAsync();
        await Expect(form.UnitType).ToHaveValueAsync("event");
        await Expect(form.UnitDefinition).ToHaveValueAsync("unknown");

        await form.UnitType.FillAsync("Time");
        await Page.GetByRole(AriaRole.Option, new() { NameRegex = TimeOption() }).ClickAsync();
        await Expect(form.UnitType).ToHaveValueAsync("Time");
        await Expect(form.UnitFields.GetByText("A job that runs on a schedule.", new() { Exact = true })).ToBeVisibleAsync();
        await Expect(form.UnitFields.GetByText("A Cron expression. The annotation time zone controls when it runs.", new() { Exact = true })).ToBeVisibleAsync();

        await form.UnitDefinition.FillAsync("*/15 * *");
        await Expect(form.UnitCronHint).ToHaveTextAsync("A Cron expression usually has 5 or 6 fields, for example */15 * * * *.");

        await form.UnitDefinition.FillAsync(string.Empty);
        await Expect(form.FieldMessage("Enter a unit definition.")).ToBeVisibleAsync();

        await form.UnitDefinition.FillAsync("0 2 * * *");
        await Expect(form.FieldMessage("Enter a unit definition.")).ToHaveCountAsync(0);
        await form.SaveButton.ClickAsync();
        await Expect(App.Toast("Annotation saved")).ToBeVisibleAsync();
        await Expect(form.UnitCronHint).ToHaveCountAsync(0);

        await form.SelectModeAsync("JSON");
        var document = await form.ReadDocumentAsync();
        if (document["UnitType"]?.GetValue<string>() != "time" || document["UnitDefinition"]?.GetValue<string>() != "0 2 * * *")
        {
            Assert.Fail($"The annotation document should hold \"UnitType\": \"time\" and \"UnitDefinition\": \"0 2 * * *\"; it is {document.ToJsonString()}.");
        }

        await Expect(form.Document.Content).ToContainTextAsync("\"UnitType\": \"time\"");
        await Expect(form.Document.Content).ToContainTextAsync("\"UnitDefinition\": \"0 2 * * *\"");

        await detail.OpenFormAsync(responsibility);
        await Expect(form.Title).ToBeVisibleAsync();
        await Expect(form.UnitFields).ToHaveCountAsync(0);
    }

    // ANN-FRM-08: Unsaved changes: Revert, Keep editing and Discard.
    // An edited title marks the form as unsaved, and Revert drops it. Edited again, leaving the detail asks "Discard
    // changes?": Keep editing stays with the draft, and Discard on the next guarded navigation (another row on a wide
    // screen, Back to the list on a phone) goes on without saving.
    [Test]
    [TestCase(219)]
    [UserRole(Role.Editor)]
    public async Task AnnotationForm_UnsavedChanges_RevertKeepOrDiscard()
    {
        var prefix = PlatformData.AnnotationKeySetPrefix(PlatformData.Subject);
        var keys = PlatformData.AnnotationKeySet(prefix, 2);
        var title = PlatformData.Title();
        var wide = Page.ViewportSize is not { Width: < DetailPanel.OverlayWidth };
        var annotations = new AnnotationsPage(App, Routes);
        var detail = new AnnotationDetail(App, Routes);
        var form = new AnnotationForm(App);
        var discard = new DiscardChangesDialog(App);
        await annotations.OpenAsync();
        await annotations.BulkCreateAsync([.. keys]);
        await annotations.WaitForCatalogAsync();
        await annotations.FilterAsync($"key:{prefix}");

        await annotations.OpenAnnotationFromListAsync(keys[0]);
        await Expect(detail.Identity).ToHaveTextAsync(keys[0]);
        await form.Title.FillAsync(title);
        await Expect(form.UnsavedChanges).ToBeVisibleAsync();
        await Expect(form.RevertButton).ToBeEnabledAsync();

        await form.RevertButton.ClickAsync();
        await Expect(form.Title).ToHaveValueAsync(string.Empty);
        await Expect(form.UnsavedChanges).ToHaveCountAsync(0);

        await form.Title.FillAsync(title);
        await detail.Panel.LeaveButton.ClickAsync();
        await discard.ExpectOpenAsync();
        await Expect(discard.Root).ToHaveAccessibleDescriptionAsync(DiscardChangesDialog.DescriptionText);

        await discard.KeepEditingAsync();
        await App.ExpectAtAsync(Routes.Annotation(keys[0]));
        await Expect(form.Title).ToHaveValueAsync(title);

        if (wide)
        {
            await annotations.AnnotationOpener(keys[1]).ClickAsync();
        }
        else
        {
            await detail.Panel.LeaveButton.ClickAsync();
        }

        await discard.ExpectOpenAsync();
        await discard.DiscardAsync();
        if (wide)
        {
            await App.ExpectAtAsync(Routes.Annotation(keys[1]));
            await Expect(detail.Identity).ToHaveTextAsync(keys[1]);
        }
        else
        {
            await App.ExpectAtAsync(Routes.Annotations);
            await Expect(detail.Panel.Root).ToHaveCountAsync(0);
        }

        await annotations.OpenAnnotationFromListAsync(keys[0]);
        await Expect(detail.Identity).ToHaveTextAsync(keys[0]);
        await Expect(form.Title).ToHaveValueAsync(string.Empty);
    }

    // "<n> of <total>" of the filter count, whatever the view's total.
    private static Regex CountOf(int shown)
    {
        return new Regex($@"^{shown} of [\d,]+$");
    }

    // The whole text of an editor once it shows.
    private static async Task<string> TextOfAsync(CodeEditor editor)
    {
        await Expect(editor.Content).ToBeVisibleAsync();
        return await editor.TextAsync();
    }

    // Empties the request recorder, so the next check sees only what follows.
    private async Task ClearRecordedRequestsAsync()
    {
        await Page.EvaluateAsync("() => { if (window.__requests) { window.__requests.length = 0; } }");
    }

    // Nothing was written to the annotation since the recorder was emptied: no POST of …/annotations/<key>.
    private async Task ExpectNoWriteAsync(string key)
    {
        var path = $"/annotations/{Uri.EscapeDataString(key)}";
        var writes = (await RecordedRequestsAsync())
            .Where(request => !string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase) && request.Url.Contains(path, StringComparison.Ordinal))
            .ToList();
        if (writes.Count > 0)
        {
            Assert.Fail($"The form should not write {key}, but it sent {string.Join(", ", writes.Select(request => $"{request.Method} {request.Url}"))}.");
        }
    }

    // The document title while the form has unsaved changes.
    [GeneratedRegex("^● ")]
    private static partial Regex DirtyTitle();

    // The description's count hint, "<n> / 10,000", shown above 9,000 characters.
    [GeneratedRegex(@"^[\d,]+ / 10,000$")]
    private static partial Regex DescriptionCount();

    // The segment "year," of a date field.
    [GeneratedRegex("^year")]
    private static partial Regex YearSegment();

    // The option "Time" of Unit type, whose name goes on with its description.
    [GeneratedRegex("^Time")]
    private static partial Regex TimeOption();
}
