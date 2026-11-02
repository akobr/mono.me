using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.KeyValues;

// Versions: the Versions tab of a key value, loaded only when it opens, View and Compare of a JSON value edited in the
// code editor, and Reveal and Compare of secret versions (each fetched on request, never cached).
[Area(Area.KeyValues)]
public sealed partial class KeyValueVersionTests : PlatformTest
{
    // KV-VER-01: A JSON value edited in the code editor: versions load on demand, View and Compare.
    // A JSON key value is edited in the code editor and saved with Ctrl/Cmd+S; its versions are requested only when the
    // Versions tab opens. View shows version 1, Compare shows the changed line of version 2 and, for version 1, the
    // addition over the empty version 0; the Value tab keeps the saved document.
    [Test]
    [TestCase(126)]
    [UserRole(Role.Editor)]
    public async Task KeyValues_JsonVersions_LoadOnDemandAndCompare()
    {
        const string disabled = "{\"enabled\":false}";
        const string enabled = "{\"enabled\":true}";
        var key = PlatformData.KeyValueKey();
        await InstallRequestRecorderAsync();

        var keyValues = new KeyValuesPage(App, Routes);
        await keyValues.OpenAsync();
        await keyValues.CreateWithContentTypeAsync(key, disabled, "JSON");

        var detail = new KeyValueDetail(App, Routes, key);
        await Expect(detail.ContentType).ToHaveTextAsync("application/json");
        await detail.JsonEditor.ExpectTextAsync(disabled);
        await Expect(detail.Value).ToHaveCountAsync(0);

        await detail.JsonEditor.ReplaceAsync(enabled);
        await Expect(detail.UnsavedChanges).ToBeVisibleAsync();
        await detail.JsonEditor.PressSaveAsync();
        await Expect(App.Toast("Saved as version 2")).ToBeVisibleAsync();
        await ExpectVersionListRequestsAsync(detail.Key, 0);

        await detail.OpenVersionsAsync();
        var versions = detail.Versions;
        await Expect(versions.Table).ToBeVisibleAsync();
        await ExpectVersionListRequestsAsync(detail.Key, 1);
        await Expect(versions.Headers).ToHaveTextAsync(["Version", "Level", "Author", "Created", "Expires", string.Empty]);
        await Expect(versions.Cell(2, "Version")).ToHaveTextAsync("v2(current)");
        await Expect(versions.Cell(2, "Expires")).ToHaveTextAsync("—");
        await Expect(versions.Cell(1, "Version")).ToHaveTextAsync("v1");
        await Expect(versions.Cell(1, "Expires")).ToHaveTextAsync(Digit());

        await versions.ViewButton(1).ClickAsync();
        await Expect(versions.VersionValue(1)).ToHaveTextAsync(disabled);

        await versions.CompareButton(2).ClickAsync();
        await Expect(versions.VersionCard(1)).ToBeHiddenAsync();
        await Expect(versions.CompareCard(1, 2)).ToBeVisibleAsync();
        await Expect(versions.DiffLine(1, 2, '−', disabled)).ToBeVisibleAsync();
        await Expect(versions.DiffLine(1, 2, '+', enabled)).ToBeVisibleAsync();

        await versions.CompareButton(1).ClickAsync();
        await Expect(versions.CompareCard(0, 1)).ToBeVisibleAsync();
        await Expect(versions.DiffLine(0, 1, '+', disabled)).ToBeVisibleAsync();

        await versions.CloseComparisonButton.ClickAsync();
        await Expect(versions.CompareCard(0, 1)).ToBeHiddenAsync();
        await detail.OpenValueAsync();
        await detail.JsonEditor.ExpectTextAsync(enabled);
    }

    // KV-VER-02: Reveal and compare secret versions.
    // The versions of a Standard secret (created, then Set new value) are masked until a version's Reveal or Compare,
    // which shows that version's value or the change between the two; the Value tab stays masked afterwards.
    [Test]
    [TestCase(127)]
    [UserRole(Role.Secrets)]
    public async Task KeyValues_SecretVersions_RevealAndCompare()
    {
        var key = PlatformData.KeyValueKey();
        var first = PlatformData.Slug("secret");
        var second = PlatformData.Slug("secret");

        var keyValues = new KeyValuesPage(App, Routes);
        await keyValues.OpenAsync();
        await keyValues.CreateSecretAsync(key, first, "Standard");

        var detail = new KeyValueDetail(App, Routes, key);
        await detail.SetNewValueAsync(second, 2);

        await detail.OpenVersionsAsync();
        var versions = detail.Versions;
        await Expect(versions.Cell(2, "Version")).ToHaveTextAsync("v2(current)");
        await Expect(versions.Cell(1, "Version")).ToHaveTextAsync("v1");
        foreach (var version in new[] { 2, 1 })
        {
            await Expect(versions.Cell(version, "Level")).ToHaveTextAsync("Standard");
            await Expect(versions.RevealButton(version)).ToBeVisibleAsync();
            await Expect(versions.CompareButton(version)).ToBeVisibleAsync();
        }

        await Expect(detail.Containing(first)).ToHaveCountAsync(0);
        await Expect(detail.Containing(second)).ToHaveCountAsync(0);

        await versions.RevealButton(1).ClickAsync();
        await Expect(versions.VersionValue(1)).ToHaveTextAsync(first);

        await versions.CloseVersionButton.ClickAsync();
        await Expect(versions.VersionCard(1)).ToBeHiddenAsync();

        await versions.CompareButton(2).ClickAsync();
        await Expect(versions.CompareCard(1, 2)).ToBeVisibleAsync();
        await Expect(versions.DiffLine(1, 2, '−', first)).ToBeVisibleAsync();
        await Expect(versions.DiffLine(1, 2, '+', second)).ToBeVisibleAsync();

        await versions.CloseComparisonButton.ClickAsync();
        await Expect(versions.CompareCard(1, 2)).ToBeHiddenAsync();
        await detail.OpenValueAsync();
        await Expect(detail.SecretValue).ToHaveTextAsync(KeyValuesPage.MaskedValue);
    }

    // The page fetched the list of the key's versions (GET …/key-values/<key>/versions, not a version or a diff below
    // it) that often since its load: the fetch recorder sees what the app sends, also where MSW's service worker
    // answers.
    private async Task ExpectVersionListRequestsAsync(string key, int expected)
    {
        var path = $"/key-values/{key}/versions";
        var count = (await RecordedRequestsAsync())
            .Count(request => request.Method == "GET" && request.Path.EndsWith(path, StringComparison.Ordinal));
        if (count != expected)
        {
            throw new InvalidOperationException($"The page requested the versions of '{key}' {count} time(s), not {expected}.");
        }
    }

    [GeneratedRegex(@"\d")]
    private static partial Regex Digit();
}
