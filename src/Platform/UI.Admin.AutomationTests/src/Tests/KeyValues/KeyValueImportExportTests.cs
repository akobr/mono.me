using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Platform.UI.Admin.AutomationTests.Support;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using Microsoft.Playwright;
using static _42.Platform.UI.Admin.AutomationTests.Support.KeyValueExportFiles;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.KeyValues;

// Import and Export: Import in three steps (source, review, result) with .env lines and Mark keys matching, a key/value
// document from a file followed by a flat JSON plan of create, update and unchanged, refused text and invalid keys, and
// Replace under a prefix with Undo deletes; Export as .env, flat JSON and the key/value document, without and with the
// secret values. Imported keys are "<prefix>.<name>" under a generated prefix; a Replace and an export with secret
// values run in a disposable project, so they never touch or carry other keys.
[Area(Area.KeyValues)]
public sealed partial class KeyValueImportExportTests : PlatformTest
{
    private const string LeftOutDescription = "The .env and flat formats carry values only; export the key/value document, or include the secret values.";
    private const string WithSecretsTitle = "The file contains secret values";
    private const string WithSecretsDescription = "Store it where it belongs, then delete it.";

    // A download of a whole format can take a while on a live view with many keys.
    private const int DownloadTimeoutMs = 30_000;

    // KV-IEX-01: Import .env lines and mark the password key as a secret.
    // .env lines become keys ('__' is '.', lower case) that all start Public; Mark keys matching "*password*" gives the
    // password the Standard level and masks its value. The dry run plans two creates, Import writes version 1 of each,
    // and the password is a Standard secret with the imported value, the host a public value.
    [Test]
    [TestCase(189)]
    [UserRole(Role.Secrets)]
    public async Task KeyValues_ImportDotEnv_MarksPasswordAsSecret()
    {
        var prefix = PlatformData.KeyValuePrefix();
        var env = PlatformData.ToEnvKey(prefix);
        var host = $"{prefix}.host";
        var password = $"{prefix}.password";
        var secret = PlatformData.Slug("secret");

        var keyValues = new KeyValuesPage(App, Routes);
        await keyValues.OpenAsync();
        var import = new KeyValueImportDialog(App);
        await import.OpenAsync();
        await Expect(import.Root).ToHaveAccessibleDescriptionAsync($"Into {Routes.Organization}/{Routes.Project}, view {Routes.View}.");
        await Expect(import.CurrentStep).ToHaveTextAsync("1. Source");
        await Expect(import.NextButton).ToBeDisabledAsync();

        await import.PasteAndReviewAsync($"{env}__HOST=redis.internal\n{env}__PASSWORD={secret}");
        await Expect(import.CurrentStep).ToHaveTextAsync("2. Review");
        await Expect(import.Label(".env")).ToBeVisibleAsync();
        await Expect(import.Label("2 row(s)")).ToBeVisibleAsync();
        await Expect(import.Level(host)).ToHaveTextAsync("Public");
        await Expect(import.RowValue(host)).ToHaveTextAsync("redis.internal");
        await Expect(import.Level(password)).ToHaveTextAsync("Public");
        await Expect(import.RowValue(password)).ToHaveTextAsync(secret);

        await Expect(import.MarkPattern).ToHaveValueAsync("*password*");
        await import.MarkAsync("Standard");
        await Expect(import.Level(password)).ToHaveTextAsync("Standard");
        await Expect(import.RowValue(password)).ToHaveTextAsync(KeyValuesPage.MaskedValue);
        await Expect(import.Level(host)).ToHaveTextAsync("Public");

        await Expect(import.ImportButton).ToBeDisabledAsync();
        await import.CheckAsync();
        await Expect(import.Plan).ToHaveTextAsync("Plan: 2 create");
        await Expect(import.Line("Create", host)).ToBeVisibleAsync();
        await Expect(import.Line("Create", password)).ToBeVisibleAsync();
        await Expect(import.ImportButton).ToBeEnabledAsync();

        await import.ImportAsync();
        await Expect(import.CurrentStep).ToHaveTextAsync("3. Result");
        await Expect(import.ResultCounts).ToHaveTextAsync("2 create");
        await ExpectVersionAsync(import.Line("Create", host), 1);
        await ExpectVersionAsync(import.Line("Create", password), 1);

        await import.DoneAsync();
        var passwordDetail = new KeyValueDetail(App, Routes, password);
        await passwordDetail.OpenAsync();
        await Expect(passwordDetail.LevelBadge).ToHaveTextAsync("Standard");
        await Expect(passwordDetail.SecretValue).ToHaveTextAsync(KeyValuesPage.MaskedValue);
        var revealed = await passwordDetail.RevealAsync();
        await Expect(revealed).ToHaveTextAsync(secret);

        var hostDetail = new KeyValueDetail(App, Routes, host);
        await hostDetail.OpenAsync();
        await Expect(hostDetail.Value).ToHaveValueAsync("redis.internal");
        await Expect(hostDetail.LevelBadge).ToHaveTextAsync("Public");
    }

    // KV-IEX-02: Import a key/value document from a file, then a flat JSON plan of create, update and unchanged.
    // A key/value document chosen as a file fills the text and keeps its levels (no Mark for a document), and its import
    // creates three keys. A flat JSON over them then plans the same value as unchanged, a new value as an update and a
    // new key as a create, and leaves the unlisted secret alone; the update makes version 2.
    [Test]
    [TestCase(190)]
    [UserRole(Role.Secrets)]
    public async Task KeyValues_ImportDocumentThenFlat_PlansEachAction()
    {
        var prefix = PlatformData.KeyValuePrefix();
        var (a, b, c, s) = ($"{prefix}.a", $"{prefix}.b", $"{prefix}.c", $"{prefix}.s");
        var secret = PlatformData.Slug("secret");
        var document = JsonSerializer.Serialize(new
        {
            Format = DocumentFormat,
            Items = new[]
            {
                new { Key = a, Protection = "Public", Value = "1" },
                new { Key = b, Protection = "Public", Value = "2" },
                new { Key = s, Protection = "Standard", Value = secret },
            },
        });

        var keyValues = new KeyValuesPage(App, Routes);
        await keyValues.OpenAsync();
        var import = new KeyValueImportDialog(App);
        await import.OpenAsync();
        await import.FileInput.SetInputFilesAsync(new FilePayload { Name = "kv.json", MimeType = "application/json", Buffer = Encoding.UTF8.GetBytes(document) });
        await Expect(import.Text).ToHaveValueAsync(document);

        await import.NextAsync();
        await Expect(import.Label("Key/value document")).ToBeVisibleAsync();
        await Expect(import.Label("3 row(s)")).ToBeVisibleAsync();
        await Expect(import.Level(s)).ToHaveTextAsync("Standard");
        await Expect(import.RowValue(s)).ToHaveTextAsync(KeyValuesPage.MaskedValue);
        await Expect(import.MarkPattern).ToHaveCountAsync(0);

        await import.CheckAsync();
        await Expect(import.Plan).ToHaveTextAsync("Plan: 3 create");
        await import.ImportAsync();
        await import.DoneAsync();

        await import.OpenAsync();
        await import.PasteAndReviewAsync(JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal) { [a] = "1", [b] = "20", [c] = "3" }));
        await Expect(import.Label("Flat JSON")).ToBeVisibleAsync();
        await Expect(import.Label("3 row(s)")).ToBeVisibleAsync();

        // The counts follow the order of the response, so each part is asserted on its own.
        await import.CheckAsync();
        foreach (var count in new[] { "1 unchanged", "1 update", "1 create" })
        {
            await Expect(import.Plan).ToContainTextAsync(count);
        }

        await Expect(import.Line("Unchanged", a)).ToBeVisibleAsync();
        await Expect(import.Line("Update", b)).ToBeVisibleAsync();
        await Expect(import.Line("Create", c)).ToBeVisibleAsync();
        await Expect(import.LinesOf(s)).ToHaveCountAsync(0);

        await import.ImportAsync();
        foreach (var count in new[] { "1 unchanged", "1 update", "1 create" })
        {
            await Expect(import.ResultCounts).ToContainTextAsync(count);
        }

        await ExpectVersionAsync(import.Line("Update", b), 2);
        await ExpectVersionAsync(import.Line("Create", c), 1);

        await import.DoneAsync();
        var updated = new KeyValueDetail(App, Routes, b);
        await updated.OpenAsync();
        await Expect(updated.Value).ToHaveValueAsync("20");
        await Expect(updated.Version).ToHaveTextAsync("v2");

        var untouched = new KeyValueDetail(App, Routes, s);
        await untouched.OpenAsync();
        await Expect(untouched.LevelBadge).ToHaveTextAsync("Standard");
        await Expect(untouched.Version).ToHaveTextAsync("v1");
    }

    // KV-IEX-03: Import refuses unreadable text and invalid keys.
    // Next needs a text, and text of no known format stays on the source with an error; an invalid .env key is counted
    // and blocks Check and Import, Back keeps the text, and without the invalid line Check is enabled. Cancel imports
    // nothing.
    [Test]
    [TestCase(191)]
    [UserRole(Role.Editor)]
    public async Task KeyValues_ImportUnreadableText_StaysOnSource()
    {
        var prefix = PlatformData.KeyValuePrefix();
        var env = PlatformData.ToEnvKey(prefix);
        var withInvalid = $"{env}__OK=1\n1BAD=2";

        var keyValues = new KeyValuesPage(App, Routes);
        await keyValues.OpenAsync();
        var import = new KeyValueImportDialog(App);
        await import.OpenAsync();
        await Expect(import.NextButton).ToBeDisabledAsync();

        await import.Text.FillAsync("hello world");
        await import.NextButton.ClickAsync();
        await Expect(import.Label(KeyValueImportDialog.UnreadableText)).ToBeVisibleAsync();
        await Expect(import.CurrentStep).ToHaveTextAsync("1. Source");
        await Expect(import.Text).ToBeVisibleAsync();
        await Expect(import.CheckButton).ToHaveCountAsync(0);

        await import.Text.FillAsync(withInvalid);
        await import.NextAsync();
        await Expect(import.Label(".env")).ToBeVisibleAsync();
        await Expect(import.Label("2 row(s)")).ToBeVisibleAsync();
        await Expect(import.Label("1 invalid key(s)")).ToBeVisibleAsync();
        await Expect(import.CheckButton).ToBeDisabledAsync();
        await Expect(import.ImportButton).ToBeDisabledAsync();

        await import.BackAsync();
        await Expect(import.CurrentStep).ToHaveTextAsync("1. Source");
        await Expect(import.Text).ToHaveValueAsync(withInvalid);

        await import.Text.FillAsync($"{env}__OK=1");
        await import.NextAsync();
        await Expect(import.Label("1 row(s)")).ToBeVisibleAsync();
        await Expect(import.InvalidCount).ToHaveCountAsync(0);
        await Expect(import.CheckButton).ToBeEnabledAsync();

        await import.BackAsync();
        await import.CancelAsync();
        var detail = new KeyValueDetail(App, Routes, $"{prefix}.ok");
        await detail.OpenAsync();
        await Expect(detail.NotFoundAlert).ToBeVisibleAsync();
    }

    // KV-IEX-04: Replace under a prefix deletes the unlisted keys; Undo deletes restores them.
    // In a disposable project, a nested JSON creates two keys under a prefix; a Replace "Only under prefix" with one of
    // them plans that one as unchanged and deletes only the other (never the key outside the prefix), after the typed
    // confirmation; Undo deletes restores it with its value.
    [Test]
    [TestCase(193)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task KeyValues_ImportReplaceUnderPrefix_UndoRestores()
    {
        var prefix = PlatformData.KeyValuePrefix();
        var dot = prefix.IndexOf('.', StringComparison.Ordinal);
        var (root, name) = (prefix[..dot], prefix[(dot + 1)..]);
        var keep = $"{prefix}.keep";
        var drop = $"{prefix}.drop";
        var outside = PlatformData.KeyValueKey();
        var nested = JsonSerializer.Serialize(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            [root] = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [name] = new Dictionary<string, string>(StringComparer.Ordinal) { ["keep"] = "1", ["drop"] = "2" },
            },
        });
        var project = await CreateDisposableProjectAsync();

        var keyValues = new KeyValuesPage(App, project);
        await keyValues.OpenAsync();
        await keyValues.CreateAsync(outside, PlatformData.Slug("value"));
        await new KeyValueDetail(App, project, outside).CloseAsync();

        var import = new KeyValueImportDialog(App);
        await import.OpenAsync();
        await import.PasteAndReviewAsync(nested);
        await Expect(import.Label("Nested JSON")).ToBeVisibleAsync();
        await Expect(import.Label("2 row(s)")).ToBeVisibleAsync();
        await Expect(import.RowValue(keep)).ToHaveTextAsync("1");
        await Expect(import.RowValue(drop)).ToHaveTextAsync("2");

        await import.CheckAsync();
        await Expect(import.Plan).ToHaveTextAsync("Plan: 2 create");
        await import.ImportAsync();
        await import.DoneAsync();

        await import.OpenAsync();
        await import.PasteAndReviewAsync(JsonSerializer.Serialize(new Dictionary<string, string>(StringComparer.Ordinal) { [keep] = "1" }));
        await Expect(import.Label("Flat JSON")).ToBeVisibleAsync();
        await import.PickAsync(import.Mode, KeyValueImportDialog.ReplaceMode);
        await import.Prefix.FillAsync($"{prefix}.");
        await Expect(import.Prefix).ToHaveValueAsync($"{prefix}.");

        // A Delete outside the prefix would delete keys the test did not create: only the dropped key may be planned.
        await import.CheckAsync();
        await Expect(import.Plan).ToContainTextAsync("1 unchanged");
        await Expect(import.Plan).ToContainTextAsync("1 delete");
        await Expect(import.Lines("Delete")).ToHaveCountAsync(1);
        await Expect(import.Line("Delete", drop)).ToBeVisibleAsync();
        await Expect(import.Line("Unchanged", keep)).ToBeVisibleAsync();
        await Expect(import.LinesOf(outside)).ToHaveCountAsync(0);

        await import.ImportButton.ClickAsync();
        var replace = import.ReplaceDialog;
        await replace.ExpectOpenAsync();
        await Expect(replace.Description).ToHaveTextAsync("1 key(s) of the view that the document does not list will be deleted (restorable for the retention period).");
        await Expect(replace.ConfirmationInput).ToHaveAccessibleNameAsync($"Type {project.View} to confirm");
        await Expect(replace.Button("Replace")).ToBeDisabledAsync();
        await replace.TypeConfirmationAsync(project.View);
        await replace.ConfirmAsync("Replace");

        await Expect(import.ResultAlert).ToBeVisibleAsync();
        await Expect(import.ResultCounts).ToContainTextAsync("1 unchanged");
        await Expect(import.ResultCounts).ToContainTextAsync("1 delete");
        await Expect(import.UndoDeletesButton).ToBeVisibleAsync();

        await import.UndoDeletesButton.ClickAsync();
        await Expect(App.Toast("1 key(s) restored")).ToBeVisibleAsync();
        await Expect(import.UndoDeletesButton).ToHaveCountAsync(0);

        await import.DoneAsync();
        var dropped = new KeyValueDetail(App, project, drop);
        await dropped.OpenAsync();
        await Expect(dropped.Value).ToHaveValueAsync("2");
        await Expect(dropped.DeletedBadge).ToHaveCountAsync(0);

        var other = new KeyValueDetail(App, project, outside);
        await other.OpenAsync();
        await Expect(other.Version).ToHaveTextAsync("v1");
    }

    // KV-IEX-05: Export .env, flat JSON and the key/value document without secret values.
    // Without "Include secret values" the .env and flat formats carry the public value and leave the secret out with a
    // warning, and the key/value document lists the secret with its level but no value; the secret's value is in no
    // file. The target view holds other keys, so only the test's two keys are looked for.
    [Test]
    [TestCase(194)]
    [UserRole(Role.Secrets)]
    public async Task KeyValues_ExportWithoutSecrets_LeavesSecretsOut()
    {
        var key = PlatformData.KeyValueKey();
        var value = PlatformData.Slug("value");
        var secretKey = PlatformData.KeyValueKey();
        var secret = PlatformData.Slug("secret");
        var fileName = $"{Routes.Organization}-{Routes.Project}-{Routes.View}";

        var keyValues = new KeyValuesPage(App, Routes);
        await keyValues.OpenAsync();
        await keyValues.CreateAsync(key, value);
        await new KeyValueDetail(App, Routes, key).CloseAsync();
        await keyValues.CreateSecretAsync(secretKey, secret, "Standard");
        await new KeyValueDetail(App, Routes, secretKey).CloseAsync();

        var export = new KeyValueExportMenu(App);
        await export.OpenAsync();
        await Expect(export.FormatItems).ToHaveTextAsync(KeyValueExportMenu.Formats);
        await Expect(export.IncludeSecretValues).ToHaveAttributeAsync("aria-checked", "false");

        var env = await DownloadExportAsync(() => export.Format(KeyValueExportMenu.DotEnv).ClickAsync(), ExpectSecretsLeftOutToastAsync);
        ExpectFileName(env, $"{fileName}.env");
        ExpectEnvLine(env, $"{PlatformData.ToEnvKey(key)}={value}");
        ExpectNoEnvLine(env, PlatformData.ToEnvKey(secretKey));
        ExpectAbsent(env, secret);

        // The .env's warning has to close first: after the document's export no toast at all may show.
        await Expect(App.ShownToasts).ToHaveCountAsync(0);
        var json = await DownloadExportAsync(() => export.ExportAsync(KeyValueExportMenu.Document), () => Expect(App.ShownToasts).ToHaveCountAsync(0));
        ExpectFileName(json, $"{fileName}.key-values.json");
        var items = DocumentItems(json);
        ExpectItem(items, key, "Public", value);
        ExpectItem(items, secretKey, "Standard", null);
        ExpectAbsent(json, secret);

        var flat = await DownloadExportAsync(() => export.ExportAsync(KeyValueExportMenu.FlatJson), ExpectSecretsLeftOutToastAsync);
        ExpectFileName(flat, $"{fileName}.flat.json");
        var values = FlatValues(flat);
        ExpectFlatValue(values, key, value);
        ExpectFlatValue(values, secretKey, null);
        ExpectAbsent(flat, secret);
    }

    // KV-IEX-06: Export with secret values on request.
    // In a disposable project (so the files carry no one else's secrets), "Include secret values" keeps the menu open
    // and stays checked between exports: every format then carries the secret's value and warns about it. A page load
    // unchecks it again.
    [Test]
    [TestCase(195)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task KeyValues_ExportWithSecrets_IncludesValues()
    {
        var key = PlatformData.KeyValueKey();
        var value = PlatformData.Slug("value");
        var secretKey = PlatformData.KeyValueKey();
        var secret = PlatformData.Slug("secret");
        var project = await CreateDisposableProjectAsync();
        var fileName = $"{project.Organization}-{project.Project}-{project.View}";

        var keyValues = new KeyValuesPage(App, project);
        await keyValues.OpenAsync();
        await keyValues.CreateAsync(key, value);
        await new KeyValueDetail(App, project, key).CloseAsync();
        await keyValues.CreateSecretAsync(secretKey, secret, "Standard");
        await new KeyValueDetail(App, project, secretKey).CloseAsync();

        var export = new KeyValueExportMenu(App);
        await export.OpenAsync();
        await export.IncludeSecretValues.ClickAsync();
        await Expect(export.IncludeSecretValues).ToHaveAttributeAsync("aria-checked", "true");
        await Expect(export.Menu).ToBeVisibleAsync();

        var flat = await DownloadExportAsync(() => export.Format(KeyValueExportMenu.FlatJson).ClickAsync(), ExpectWithSecretsToastAsync);
        ExpectFileName(flat, $"{fileName}.flat.json");
        var values = FlatValues(flat);
        ExpectFlatValue(values, secretKey, secret);
        ExpectFlatValue(values, key, value);

        // Each export's toast is awaited alone: the one before closes first.
        await Expect(App.ShownToasts).ToHaveCountAsync(0);
        await export.OpenAsync();
        await Expect(export.IncludeSecretValues).ToHaveAttributeAsync("aria-checked", "true");
        var env = await DownloadExportAsync(() => export.Format(KeyValueExportMenu.DotEnv).ClickAsync(), ExpectWithSecretsToastAsync);
        ExpectFileName(env, $"{fileName}.env");
        ExpectEnvLine(env, $"{PlatformData.ToEnvKey(secretKey)}={secret}");

        await Expect(App.ShownToasts).ToHaveCountAsync(0);
        var json = await DownloadExportAsync(() => export.ExportAsync(KeyValueExportMenu.Document), ExpectWithSecretsToastAsync);
        ExpectFileName(json, $"{fileName}.key-values.json");
        ExpectItem(DocumentItems(json), secretKey, "Standard", secret);

        await Page.ReloadAsync();
        await App.WaitForReadyAsync();
        await export.OpenAsync();
        await Expect(export.IncludeSecretValues).ToHaveAttributeAsync("aria-checked", "false");
    }

    // Runs the action of an export, checks the toasts it shows as soon as the download starts and then reads the file
    // over the browser's connection (as PlatformTest.DownloadAsync does): a toast closes after about 5 seconds, and
    // reading a download can take longer than that on a loaded machine. The file is not kept as an artifact.
    private async Task<DownloadedFile> DownloadExportAsync(Func<Task> action, Func<Task> expectToasts)
    {
        var download = await Page.RunAndWaitForDownloadAsync(action, new() { Timeout = DownloadTimeoutMs });
        await expectToasts();
        await using var stream = await download.CreateReadStreamAsync()
            ?? throw new InvalidOperationException($"The download of {download.SuggestedFilename} has no content: {await download.FailureAsync()}");
        using var content = new MemoryStream();
        await stream.CopyToAsync(content);
        return new DownloadedFile(download.SuggestedFilename, content.ToArray());
    }

    // The warning "<n> secret(s) left out" and its own description.
    private async Task ExpectSecretsLeftOutToastAsync()
    {
        await Expect(App.Toast(SecretsLeftOut())).ToBeVisibleAsync();
        var toast = App.ShownToasts.Filter(new() { Has = Page.GetByText(SecretsLeftOut()) }).Last;
        await Expect(toast.Locator("[data-slot='description']")).ToHaveTextAsync(LeftOutDescription);
    }

    private async Task ExpectWithSecretsToastAsync()
    {
        await Expect(App.Toast(WithSecretsTitle)).ToBeVisibleAsync();
        await Expect(App.ToastDescriptionOf(WithSecretsTitle)).ToHaveTextAsync(WithSecretsDescription);
    }

    // The import's line (plan or result) shows the version it wrote, "v<n>".
    private static async Task ExpectVersionAsync(ILocator line, int version)
    {
        await Expect(line.GetByText($"v{version}", new() { Exact = true })).ToBeVisibleAsync();
    }

    // "<n> secret(s) left out": the view's other secrets count too.
    [GeneratedRegex(@"^\d+ secret\(s\) left out$")]
    private static partial Regex SecretsLeftOut();
}
