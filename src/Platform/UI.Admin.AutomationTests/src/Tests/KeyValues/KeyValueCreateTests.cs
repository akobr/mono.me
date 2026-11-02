using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.KeyValues;

// Create: the New key dialog of the values store, a public key value read back, the key grammar and the size of the
// value, a key that exists already, and a secret reference with its Reference badge.
[Area(Area.KeyValues)]
public sealed partial class KeyValueCreateTests : PlatformTest
{
    // The message of the dialog for a key against the grammar or longer than 128 characters.
    private const string KeyGrammarError = "Use dot-separated segments of letters, digits and underscores (not starting with a digit), at most 128 characters.";

    // KV-CRE-01: Create a public key value and read it back.
    // A new public key value (its key "at_<runId>.<word>_<hex4>": the store allows no '-') opens its detail with the
    // value, the Public badge, version 1 and its binding, and its Versions tab has the first version as the current one.
    [Test]
    [Elementary]
    [TestCase(4)]
    [UserRole(Role.Editor)]
    public async Task KeyValues_CreateAndRead()
    {
        var key = PlatformData.KeyValueKey();
        var value = PlatformData.Slug("value");

        var keyValues = new KeyValuesPage(App, Routes);
        await keyValues.OpenAsync();
        await Expect(keyValues.Search).ToHaveAttributeAsync("placeholder", "Search keys and public values");
        await Expect(keyValues.ExportButton).ToBeVisibleAsync();
        await Expect(keyValues.ImportButton).ToBeVisibleAsync();
        await Expect(keyValues.NewKeyButton).ToBeVisibleAsync();

        await keyValues.OpenNewKeyAsync();
        await Expect(keyValues.NewKeyDialog).ToHaveAccessibleDescriptionAsync($"In {Routes.Organization}/{Routes.Project}, view {Routes.View}.");
        await Expect(keyValues.NewKeyLevel).ToHaveTextAsync("Public");
        await Expect(keyValues.NewKeyContentType).ToHaveTextAsync("Text");
        await keyValues.NewKeyName.FillAsync(key);
        await keyValues.NewKeyValue.FillAsync(value);
        await keyValues.CreateButton.ClickAsync();
        await Expect(keyValues.NewKeyDialog).ToBeHiddenAsync();
        await App.ExpectAtAsync(Routes.KeyValue(key));

        var detail = new KeyValueDetail(App, Routes, key);
        await Expect(detail.Heading).ToBeVisibleAsync();
        await Expect(keyValues.Value(key)).ToHaveValueAsync(value);
        await Expect(detail.LevelBadge).ToHaveTextAsync("Public");
        await Expect(detail.Version).ToHaveTextAsync("v1");
        await Expect(detail.Binding).ToHaveTextAsync($"@{detail.Key}");

        await keyValues.OpenVersionsAsync(key);
        await Expect(keyValues.CurrentVersion).ToContainTextAsync("v1(current)");
        await Expect(detail.Versions.Cell(1, "Version")).ToHaveTextAsync("v1(current)");
        await Expect(detail.Versions.Cell(1, "Level")).ToHaveTextAsync("Public");
        await Expect(detail.Versions.Cell(1, "Expires")).ToHaveTextAsync("—");
    }

    // KV-CRE-02: New key validates the key grammar and the value size.
    // The dialog refuses an empty key, a key against the grammar (a digit first, a '-', an empty segment) and one of 129
    // characters, then a value one byte over 24,576, in the browser and with the dialog open; after Cancel the key does
    // not exist, and its URL shows "Not found", also after Retry.
    [Test]
    [TestCase(15)]
    [UserRole(Role.Editor)]
    public async Task KeyValues_NewKeyInvalidInput_ShowsFieldErrors()
    {
        var key = PlatformData.KeyValueKey();
        var tooLongKey = $"at_x.{new string('k', 124)}";

        var keyValues = new KeyValuesPage(App, Routes);
        await keyValues.OpenAsync();
        await keyValues.OpenNewKeyAsync();
        await keyValues.CreateButton.ClickAsync();
        await keyValues.ExpectNewKeyErrorAsync(keyValues.NewKeyName, "Enter a key.");
        await Expect(keyValues.NewKeyDialog).ToBeVisibleAsync();

        foreach (var invalid in new[] { "1st.key", "db-password", "a..b", tooLongKey })
        {
            await keyValues.NewKeyName.FillAsync(invalid);
            await keyValues.CreateButton.ClickAsync();
            await keyValues.ExpectNewKeyErrorAsync(keyValues.NewKeyName, KeyGrammarError);
            await Expect(keyValues.NewKeyDialog).ToBeVisibleAsync();
        }

        await keyValues.NewKeyName.FillAsync(key);
        await keyValues.NewKeyValue.FillAsync(new string('x', 24_577));
        await keyValues.CreateButton.ClickAsync();
        await keyValues.ExpectNewKeyErrorAsync(keyValues.NewKeyValue, "The value is longer than 24576 bytes.");
        await keyValues.ExpectNoNewKeyErrorAsync(keyValues.NewKeyName);
        await Expect(keyValues.NewKeyDialog).ToBeVisibleAsync();

        await keyValues.NewKeyCancelButton.ClickAsync();
        await Expect(keyValues.NewKeyDialog).ToBeHiddenAsync();
        await App.ExpectAtAsync(Routes.KeyValues);

        var detail = new KeyValueDetail(App, Routes, key);
        await detail.OpenAsync();
        await ExpectNotFoundAsync(detail);
        await detail.RetryButton.ClickAsync();
        await ExpectNotFoundAsync(detail);
    }

    // KV-CRE-03: New key with the name of an existing key shows a field error.
    // New key sends ExpectedVersion 0, so a key that exists already, also in upper case (keys are unique regardless
    // of case), is refused with an error under Key and the dialog stays open; the key keeps its value and its only
    // version.
    [Test]
    [TestCase(45)]
    [UserRole(Role.Editor)]
    public async Task KeyValues_NewKeyExistingName_ShowsKeyError()
    {
        const string existsError = "This key exists already; open it to change its value.";
        var key = PlatformData.KeyValueKey();
        var first = PlatformData.Slug("value");
        var second = PlatformData.Slug("value");

        var keyValues = new KeyValuesPage(App, Routes);
        await keyValues.OpenAsync();
        await keyValues.CreateAsync(key, first);

        var detail = new KeyValueDetail(App, Routes, key);
        await detail.CloseAsync();
        await Expect(detail.Region).ToBeHiddenAsync();

        await keyValues.OpenNewKeyAsync();
        await keyValues.NewKeyName.FillAsync(key);
        await keyValues.NewKeyValue.FillAsync(second);
        await keyValues.CreateButton.ClickAsync();
        await keyValues.ExpectNewKeyErrorAsync(keyValues.NewKeyName, existsError);
        await Expect(keyValues.NewKeyDialog).ToBeVisibleAsync();

        // The changed key is valid, so the dialog drops the error before it asks the API again.
        await keyValues.NewKeyName.FillAsync(key.ToUpperInvariant());
        await keyValues.ExpectNoNewKeyErrorAsync(keyValues.NewKeyName);
        await keyValues.CreateButton.ClickAsync();
        await keyValues.ExpectNewKeyErrorAsync(keyValues.NewKeyName, existsError);
        await Expect(keyValues.NewKeyDialog).ToBeVisibleAsync();

        await keyValues.NewKeyCancelButton.ClickAsync();
        await Expect(keyValues.NewKeyDialog).ToBeHiddenAsync();
        await detail.OpenAsync();
        await Expect(detail.Value).ToHaveValueAsync(first);
        await Expect(detail.Version).ToHaveTextAsync("v1");

        await detail.OpenVersionsAsync();
        await Expect(detail.Versions.Row(1)).ToBeVisibleAsync();
        await Expect(detail.Versions.Rows).ToHaveCountAsync(1);
        await Expect(detail.Versions.Cell(1, "Version")).ToHaveTextAsync("v1(current)");
    }

    // KV-CRE-04: A secret reference is validated and shows the Reference badge.
    // The content type "Secret reference" asks for a Key Vault reference: the API refuses another value, and the dialog
    // refuses a reference with a secret level before it asks the API. A public reference gets the Reference badge, its
    // content type and its help, and the plain binding.
    [Test]
    [TestCase(47)]
    [UserRole(Role.Secrets)]
    public async Task KeyValues_NewSecretReference_ShowsReferenceBadge()
    {
        var key = PlatformData.KeyValueKey();
        var reference = PlatformData.SecretReference();

        var keyValues = new KeyValuesPage(App, Routes);
        await keyValues.OpenAsync();
        await keyValues.OpenNewKeyAsync();
        await App.PickOptionAsync(keyValues.NewKeyContentType, "Secret reference");
        await Expect(keyValues.NewKeyText("kv://<vault>/<name> or https://<vault>.vault.azure.net/secrets/<name>")).ToBeVisibleAsync();
        await Expect(keyValues.NewKeyValue).ToHaveAttributeAsync("placeholder", "kv://my-vault/db-password");

        await keyValues.NewKeyName.FillAsync(key);
        await keyValues.NewKeyValue.FillAsync("not-a-ref");
        await keyValues.CreateButton.ClickAsync();
        await keyValues.ExpectNewKeyErrorAsync(keyValues.NewKeyValue, SecretReferenceMessage());
        await Expect(keyValues.NewKeyDialog).ToBeVisibleAsync();

        await keyValues.NewKeySecret.SetCheckedAsync(true);
        await Expect(keyValues.NewKeyLevel).Not.ToHaveTextAsync("Public");
        await keyValues.NewKeyValue.FillAsync(reference);
        await keyValues.CreateButton.ClickAsync();
        await keyValues.ExpectNewKeyErrorAsync(keyValues.NewKeyContentType, "A reference is a public pair; the platform never resolves it.");
        await Expect(keyValues.NewKeyDialog).ToBeVisibleAsync();

        await keyValues.NewKeySecret.SetCheckedAsync(false);
        await Expect(keyValues.NewKeyLevel).ToHaveTextAsync("Public");
        await keyValues.CreateButton.ClickAsync();
        await Expect(keyValues.NewKeyDialog).ToBeHiddenAsync();
        await App.ExpectAtAsync(Routes.KeyValue(key));

        var detail = new KeyValueDetail(App, Routes, key);
        await Expect(detail.LevelBadge).ToHaveTextAsync("Reference");
        await Expect(detail.ContentType).ToHaveTextAsync("application/vnd.2s.secret-reference");
        await Expect(detail.Value).ToHaveValueAsync(reference);
        await Expect(detail.Text("A reference to a secret in your own Key Vault; the application resolves it with its own identity.")).ToBeVisibleAsync();
        await Expect(detail.Binding).ToHaveTextAsync($"@{detail.Key}");
    }

    private async Task ExpectNotFoundAsync(KeyValueDetail detail)
    {
        await Expect(detail.NotFoundAlert).ToBeVisibleAsync();
        await Expect(detail.NotFoundDescription).ToHaveTextAsync($"The key '{detail.Key}' does not exist in view '{Routes.View}'.");
        await Expect(detail.RetryButton).ToBeVisibleAsync();
    }

    // The API's refusal of a value that is no Key Vault reference: the mock's "'…' is not a Key Vault secret reference
    // (…)", the API's "The secret reference is not valid: …".
    [GeneratedRegex("secret reference", RegexOptions.IgnoreCase)]
    private static partial Regex SecretReferenceMessage();
}
