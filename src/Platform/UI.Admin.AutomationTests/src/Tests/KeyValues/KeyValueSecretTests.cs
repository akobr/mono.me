using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.KeyValues;

// Secrets: server-side secrets and their protection. Reveal, Hide and the secret's copy, the re-mask after 60
// seconds, Set new value and a change to Public, Change protection to HSM, a HYOK value encrypted in the browser for
// a recipient, and bulk Change protection with a key it cannot change. The cases that need a level outside the
// default policy (Public and Standard) or a policy change run in a disposable project.
[Area(Area.KeyValues)]
public sealed partial class KeyValueSecretTests : PlatformTest
{
    // What the API answers a policy with HSM where the deployment has no HSM provider (501 ProtectionNotConfigured).
    private const string HsmNotConfigured = "The protection level 'Hsm' is not configured in this deployment.";

    // The warning toast of the secret's copy button.
    private const string SecretCopiedTitle = "The clipboard contains a secret";

    // The warning toast of the bulk Change protection of KV-SEC-06; its description lists "<key>: <message>".
    private const string NotDoneTitle = "1 of 2 keys were not done";

    // KV-SEC-01: Create a Standard secret, reveal, copy and hide it.
    // A Standard secret is masked in its detail until Reveal, which shows it for 60 seconds with a copy button that
    // warns about the secret in the clipboard; Hide and a page load mask it again, and the value is never on the page
    // while masked.
    [Test]
    [TestCase(84)]
    [UserRole(Role.Secrets)]
    public async Task KeyValues_StandardSecret_RevealsCopiesAndHides()
    {
        var key = PlatformData.KeyValueKey();
        var secret = PlatformData.Slug("secret");
        await InstallClipboardRecorderAsync();

        var keyValues = new KeyValuesPage(App, Routes);
        await keyValues.OpenAsync();
        await keyValues.OpenNewKeyAsync();
        await keyValues.NewKeySecret.SetCheckedAsync(true);
        await keyValues.PickAsync(keyValues.NewKeyLevel, "Standard");
        await Expect(keyValues.NewKeyText("Encrypted in the database with a software key of the project in Azure Key Vault.")).ToBeVisibleAsync();

        await keyValues.NewKeyName.FillAsync(key);
        await keyValues.NewKeyValue.FillAsync(secret);
        await keyValues.CreateButton.ClickAsync();
        await Expect(keyValues.NewKeyDialog).ToBeHiddenAsync();
        await App.ExpectAtAsync(Routes.KeyValue(key));

        var detail = new KeyValueDetail(App, Routes, key);
        await Expect(detail.LevelBadge).ToHaveTextAsync("Standard");
        await Expect(detail.SecretValue).ToHaveTextAsync(KeyValuesPage.MaskedValue);
        await Expect(detail.Containing(secret)).ToHaveCountAsync(0);
        await Expect(detail.RevealButton).ToBeVisibleAsync();
        await Expect(detail.SetNewValueButton).ToBeVisibleAsync();
        await Expect(detail.SecretCopyButton).ToHaveCountAsync(0);

        var revealed = await detail.RevealAsync();
        await Expect(revealed).ToHaveTextAsync(secret);
        await Expect(detail.MaskedAgainHint).ToBeVisibleAsync();
        await Expect(detail.HideButton).ToBeVisibleAsync();
        await Expect(detail.SecretCopyButton).ToBeVisibleAsync();

        await detail.SecretCopyButton.ClickAsync();
        await Expect(App.Toast(SecretCopiedTitle)).ToBeVisibleAsync();
        await Expect(App.ToastDescriptionOf(SecretCopiedTitle)).ToHaveTextAsync("Paste it where it belongs, then clear the clipboard.");
        await WaitForCopiedTextAsync(secret);

        await detail.HideButton.ClickAsync();
        await Expect(detail.SecretValue).ToHaveTextAsync(KeyValuesPage.MaskedValue);
        await Expect(detail.Containing(secret)).ToHaveCountAsync(0);
        await Expect(detail.RevealButton).ToBeVisibleAsync();
        await Expect(detail.MaskedAgainHint).ToBeHiddenAsync();
        await Expect(detail.SecretCopyButton).ToHaveCountAsync(0);

        await detail.OpenAsync();
        await Expect(detail.SecretValue).ToHaveTextAsync(KeyValuesPage.MaskedValue);
        await Expect(App.Page.GetByText(secret)).ToHaveCountAsync(0);
    }

    // KV-SEC-02: A revealed secret is masked again after 60 seconds.
    // With the page's clock under the test's control, a revealed Standard secret is still shown after 59 seconds and
    // masked again, with Reveal back, after 61.
    [Test]
    [TestCase(85)]
    [UserRole(Role.Secrets)]
    public async Task KeyValues_RevealedSecret_MasksAfterSixtySeconds()
    {
        var key = PlatformData.KeyValueKey();
        var secret = PlatformData.Slug("secret");
        await Page.Clock.InstallAsync();

        var keyValues = new KeyValuesPage(App, Routes);
        await keyValues.OpenAsync();
        await keyValues.CreateSecretAsync(key, secret, "Standard");

        // The clock is paused (a second ahead of the page's time) before Reveal, so the 60-second timer counts only the
        // fast-forwards, not the real time of the reveal's round trips and of the assertions (the reveal itself, a fetch
        // and Vue's microtasks, needs no timer).
        var detail = new KeyValueDetail(App, Routes, key);
        var now = await Page.EvaluateAsync<long>("() => Date.now()");
        await Page.Clock.PauseAtAsync(DateTimeOffset.FromUnixTimeMilliseconds(now + 1000).UtcDateTime);
        var revealed = await detail.RevealAsync();
        await Expect(revealed).ToHaveTextAsync(secret);
        await Expect(detail.MaskedAgainHint).ToBeVisibleAsync();

        await Page.Clock.FastForwardAsync("00:59");
        await Expect(detail.SecretValue).ToHaveTextAsync(secret);

        await Page.Clock.FastForwardAsync("00:02");
        await Expect(detail.SecretValue).ToHaveTextAsync(KeyValuesPage.MaskedValue);
        await Expect(detail.RevealButton).ToBeVisibleAsync();
        await Expect(detail.MaskedAgainHint).ToBeHiddenAsync();
    }

    // KV-SEC-03: Set a new value for a secret, then make it public.
    // Set new value opens a level and a value (Cancel drops them); a save makes a masked version 2. A change of the
    // level to Public warns that the secret becomes public, and its save turns the key into a public value in the
    // textbox; the older versions keep their Standard level.
    [Test]
    [TestCase(86)]
    [UserRole(Role.Secrets)]
    public async Task KeyValues_SetNewSecretValue_SavesAndMakesPublic()
    {
        var key = PlatformData.KeyValueKey();
        var first = PlatformData.Slug("secret");
        var second = PlatformData.Slug("secret");
        var publicValue = PlatformData.Slug("value");

        var keyValues = new KeyValuesPage(App, Routes);
        await keyValues.OpenAsync();
        await keyValues.CreateSecretAsync(key, first, "Standard");

        var detail = new KeyValueDetail(App, Routes, key);
        await detail.SetNewValueButton.ClickAsync();
        await Expect(detail.Text("New level")).ToBeVisibleAsync();
        await Expect(detail.NewLevel).ToHaveTextAsync("Standard");
        await Expect(detail.NewValue).ToBeVisibleAsync();
        await Expect(detail.CancelButton).ToBeVisibleAsync();
        await Expect(detail.SaveButton).ToBeDisabledAsync();

        await detail.NewValue.FillAsync(second);
        await Expect(detail.UnsavedChanges).ToBeVisibleAsync();
        await detail.CancelButton.ClickAsync();
        await Expect(detail.NewLevel).ToBeHiddenAsync();
        await Expect(detail.NewValue).ToBeHiddenAsync();
        await Expect(detail.SetNewValueButton).ToBeVisibleAsync();

        await detail.SetNewValueAsync(second, 2);
        await Expect(detail.SecretValue).ToHaveTextAsync(KeyValuesPage.MaskedValue);
        await Expect(detail.Version).ToHaveTextAsync("v2");

        var revealed = await detail.RevealAsync();
        await Expect(revealed).ToHaveTextAsync(second);

        await detail.SetNewValueButton.ClickAsync();
        await keyValues.PickAsync(detail.NewLevel, "Public");
        await Expect(detail.Text("Older versions keep their Standard level until they expire. The secret becomes public for every reader.")).ToBeVisibleAsync();

        await detail.NewValue.FillAsync(publicValue);
        await detail.SaveAsync(3);
        await Expect(detail.LevelBadge).ToHaveTextAsync("Public");
        await Expect(detail.Value).ToHaveValueAsync(publicValue);
        await Expect(detail.RevealButton).ToHaveCountAsync(0);
        await Expect(detail.SetNewValueButton).ToHaveCountAsync(0);

        await detail.OpenVersionsAsync();
        var versions = detail.Versions;
        await Expect(versions.Cell(3, "Version")).ToHaveTextAsync("v3(current)");
        await Expect(versions.Cell(3, "Level")).ToHaveTextAsync("Public");
        await Expect(versions.ViewButton(3)).ToBeVisibleAsync();
        foreach (var version in new[] { 2, 1 })
        {
            await Expect(versions.Cell(version, "Level")).ToHaveTextAsync("Standard");
            await Expect(versions.RevealButton(version)).ToBeVisibleAsync();
        }
    }

    // KV-SEC-04: Change the protection of a secret to HSM.
    // In a disposable project that allows HSM, a Standard secret moves to HSM through Change protection: a new version
    // with the same value, while version 1 keeps Standard, so the comparison of the two finds no difference. A
    // deployment without an HSM provider refuses to allow HSM; the test is then skipped with the API's message.
    [Test]
    [TestCase(87)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task KeyValues_ChangeProtectionToHsm_KeepsValue()
    {
        var key = PlatformData.KeyValueKey();
        var secret = PlatformData.Slug("secret");
        var project = await CreateDisposableProjectAsync();

        var settings = new ProjectSettingsPage(App, project);
        await settings.OpenAsync("secrets");
        await settings.AllowLevelAsync("HSM");
        var refusal = await settings.SaveSecretsPolicyOrReadRefusalAsync();
        if (refusal == HsmNotConfigured)
        {
            Skip.Test(refusal);
        }
        else if (refusal is not null)
        {
            throw new InvalidOperationException($"The secrets policy with HSM of the project {project.Project} was refused: {refusal}");
        }

        var keyValues = new KeyValuesPage(App, project);
        await keyValues.OpenAsync();
        await keyValues.CreateSecretAsync(key, secret, "Standard");

        var detail = new KeyValueDetail(App, project, key);
        await Expect(detail.LevelBadge).ToHaveTextAsync("Standard");
        await Expect(detail.ChangeProtectionButton).ToBeVisibleAsync();

        await detail.ChangeProtectionButton.ClickAsync();
        await Expect(detail.MoveTo("HSM")).ToBeVisibleAsync();
        await Expect(detail.MoveTo("Standard")).ToHaveCountAsync(0);

        await detail.MoveTo("HSM").ClickAsync();
        var move = detail.MoveDialog("HSM");
        await move.ExpectOpenAsync();
        await Expect(move.Description).ToHaveTextAsync("The value is encrypted again under the new level as a new version. Older versions keep their previous level until they expire.");
        await move.ConfirmAsync("Change protection");
        await Expect(App.Toast($"'{detail.Key}' is HSM now")).ToBeVisibleAsync();
        await Expect(detail.LevelBadge).ToHaveTextAsync("HSM");
        await Expect(detail.Version).ToHaveTextAsync("v2");

        var revealed = await detail.RevealAsync();
        await Expect(revealed).ToHaveTextAsync(secret);

        await detail.OpenVersionsAsync();
        var versions = detail.Versions;
        await Expect(versions.Cell(2, "Version")).ToHaveTextAsync("v2(current)");
        await Expect(versions.Cell(2, "Level")).ToHaveTextAsync("HSM");
        await Expect(versions.Cell(1, "Level")).ToHaveTextAsync("Standard");

        await versions.CompareButton(2).ClickAsync();
        await Expect(versions.CompareCard(1, 2)).ToBeVisibleAsync();
        await Expect(versions.CompareCard(1, 2).GetByText("No differences.", new() { Exact = true })).ToBeVisibleAsync();
    }

    // KV-SEC-05: A HYOK value is encrypted in the browser for the recipients.
    // In a disposable project with a recipient (a real 3072-bit RSA key as JWK) and HYOK allowed, a HYOK value leaves
    // the browser only encrypted: the create's PUT carries Protection Hyok without the plaintext, the detail shows the
    // recipient instead of the value, a new value is encrypted the same way, and the versions offer no View, Reveal or
    // Compare.
    [Test]
    [TestCase(89)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task KeyValues_HyokValue_EncryptsInBrowser()
    {
        const string oneRecipient = "Encrypted in your browser for 1 recipient(s).";
        var key = PlatformData.KeyValueKey();
        var value = PlatformData.Slug("hyok");
        var newValue = PlatformData.Slug("hyok");
        var recipient = PlatformData.RecipientId();
        await InstallRequestRecorderAsync();
        var project = await CreateDisposableProjectAsync();

        var settings = new ProjectSettingsPage(App, project);
        await settings.OpenAsync("secrets");
        await settings.AddRecipientAsync(PlatformData.Title(), recipient, PlatformData.RsaPublicJwk());
        await settings.AllowLevelAsync("HYOK");
        await settings.SaveSecretsPolicyAsync();

        var keyValues = new KeyValuesPage(App, project);
        await keyValues.OpenAsync();
        await keyValues.OpenNewKeyAsync();
        await keyValues.NewKeySecret.SetCheckedAsync(true);
        await keyValues.PickAsync(keyValues.NewKeyLevel, "HYOK");
        await Expect(keyValues.NewKeyText(oneRecipient)).ToBeVisibleAsync();

        await keyValues.NewKeyName.FillAsync(key);
        await keyValues.NewKeyValue.FillAsync(value);
        await keyValues.CreateButton.ClickAsync();
        await Expect(keyValues.NewKeyDialog).ToBeHiddenAsync();
        await App.ExpectAtAsync(project.KeyValue(key));

        var detail = new KeyValueDetail(App, project, key);
        await Expect(detail.LevelBadge).ToHaveTextAsync("HYOK");
        await Expect(detail.ClientEncryptedAlert).ToBeVisibleAsync();
        await Expect(detail.ClientEncryptedDescription).ToHaveTextAsync("The platform stores a document encrypted for 1 recipient(s) and cannot read it. Only the holder of a recipient's private key decrypts it.");
        await Expect(detail.Text(recipient)).ToBeVisibleAsync();

        var puts = (await RecordedRequestsAsync())
            .Where(request => request.Method == "PUT" && new Uri(request.Url).AbsolutePath.EndsWith($"/key-values/{detail.Key}", StringComparison.Ordinal))
            .ToList();
        if (puts.Count != 1)
        {
            throw new InvalidOperationException($"The create of '{detail.Key}' sent {puts.Count} PUT request(s), not one.");
        }

        var body = puts[0].Body ?? string.Empty;
        if (JsonNode.Parse(body)?["Protection"]?.GetValue<string>() != "Hyok" || body.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The create of '{detail.Key}' did not send an encrypted HYOK value: {body}");
        }

        await Expect(App.Page.GetByText(value)).ToHaveCountAsync(0);
        await Expect(detail.RevealButton).ToHaveCountAsync(0);
        await Expect(detail.ChangeProtectionButton).ToHaveCountAsync(0);

        await detail.SetNewValueButton.ClickAsync();
        await Expect(detail.NewLevel).ToHaveTextAsync("HYOK");
        await Expect(detail.Text(oneRecipient)).ToBeVisibleAsync();
        await detail.NewValue.FillAsync(newValue);
        await detail.SaveAsync(2);

        await detail.OpenVersionsAsync();
        var versions = detail.Versions;
        await Expect(versions.ClientEncryptedNotice).ToBeVisibleAsync();
        await Expect(versions.VersionCells).ToHaveTextAsync(["v2(current)", "v1"]);
        await Expect(versions.Cell(2, "Level")).ToHaveTextAsync("HYOK");
        await Expect(versions.Cell(1, "Level")).ToHaveTextAsync("HYOK");
        await Expect(versions.RowButtons).ToHaveCountAsync(0);
    }

    // KV-SEC-06: Bulk Change protection keeps the keys it could not change selected.
    // In a disposable project (Public and Standard), bulk Change protection of a public key and a Standard secret to
    // Standard offers only Standard; the public key fails and stays selected, the secret is unchanged (done, without a
    // new version), and both keep their level and version 1.
    [Test]
    [TestCase(90)]
    [UserRole(Role.Owner)]
    [DesktopOnly]
    [DisposableProject]
    public async Task KeyValues_BulkChangeProtection_KeepsFailedSelected()
    {
        var publicKey = PlatformData.KeyValueKey();
        var secretKey = PlatformData.KeyValueKey();
        var project = await CreateDisposableProjectAsync();

        var keyValues = new KeyValuesPage(App, project);
        await keyValues.OpenAsync();
        await keyValues.CreateAsync(publicKey, PlatformData.Slug("value"));
        await new KeyValueDetail(App, project, publicKey).CloseAsync();
        await keyValues.CreateSecretAsync(secretKey, PlatformData.Slug("secret"), "Standard");
        await new KeyValueDetail(App, project, secretKey).CloseAsync();

        await keyValues.SelectKey(publicKey).CheckAsync();
        await keyValues.SelectKey(secretKey).CheckAsync();
        await Expect(keyValues.BulkSelected(2)).ToBeVisibleAsync();
        await Expect(keyValues.BulkActionButton("Delete")).ToBeVisibleAsync();
        await Expect(keyValues.BulkActionButton("Change protection")).ToBeVisibleAsync();
        await Expect(keyValues.BulkActionButton("Clear selection")).ToBeVisibleAsync();

        await keyValues.BulkActionButton("Change protection").ClickAsync();
        var levels = keyValues.OpenMenuItems;
        await Expect(levels).ToHaveTextAsync(["Standard"]);

        await levels.First.ClickAsync();
        var move = new ConfirmDialog(App, "Move 2 key(s) to Standard?");
        await move.ExpectOpenAsync();
        await Expect(move.Description).ToHaveTextAsync("Each value is encrypted again under the new level as a new version; older versions keep their level until they expire. Public and HYOK keys are skipped.");
        await move.ConfirmAsync("Change protection");
        await Expect(App.Toast(NotDoneTitle)).ToBeVisibleAsync();
        await Expect(App.ToastDescriptionOf(NotDoneTitle)).ToHaveTextAsync(new Regex($"^{Regex.Escape(publicKey)}: "));
        await Expect(keyValues.BulkSelected(1)).ToBeVisibleAsync();
        await Expect(keyValues.SelectKey(publicKey)).ToBeCheckedAsync();
        await Expect(keyValues.SelectKey(secretKey)).Not.ToBeCheckedAsync();

        await keyValues.BulkActionButton("Clear selection").ClickAsync();
        await Expect(keyValues.BulkActions).ToBeHiddenAsync();

        await Expect(keyValues.ItemCell(publicKey, "Level")).ToHaveTextAsync("Public");
        await Expect(keyValues.ItemCell(publicKey, "Version")).ToHaveTextAsync("v1");
        await Expect(keyValues.ItemCell(secretKey, "Level")).ToHaveTextAsync("Standard");
        await Expect(keyValues.ItemCell(secretKey, "Version")).ToHaveTextAsync("v1");
    }
}
