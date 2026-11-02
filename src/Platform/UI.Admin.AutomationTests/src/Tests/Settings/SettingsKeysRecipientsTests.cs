using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Data;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Settings;

// Keys and Recipients: the Key-encryption keys card (rotating the Standard key after a confirmation, allowing HSM,
// which creates the HSM key, and rotating that one) and the Recipients (HYOK) card (adding RSA keys of 3072 bits,
// refusing invalid ones, disabling, enabling and removing), in a disposable project. A deployed API re-wraps after a
// rotation in its hourly maintenance, so the tests do not wait for it; where the deployment has no HSM provider, the
// HSM test is skipped with the API's message.
[Area(Area.Settings)]
public sealed partial class SettingsKeysRecipientsTests : PlatformTest
{
    // SET-KEY-01: Administrator rotates the Standard key after confirming.
    // A new project with a Standard secret has a Standard key (a deployed one gets it with the first secret) and no
    // HSM key. "Rotate now" asks first; Cancel keeps the version, Rotate gives a new one, kept after a reload, and the
    // re-wrap alert targets it.
    [Test]
    [TestCase(197)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task SecretKeys_RotateStandardKey_ShowsNewVersion()
    {
        var project = await CreateDisposableProjectAsync();
        var key = PlatformData.KeyValueKey();

        var keyValues = new KeyValuesPage(App, project);
        await keyValues.OpenAsync();
        await keyValues.CreateSecretAsync(key, PlatformData.Slug("secret"));
        await Expect(keyValues.LevelBadge(key)).ToHaveTextAsync("Standard");

        var settings = new ProjectSettingsPage(App, project);
        var keys = new KeyEncryptionKeysCard(App);
        await OpenUntilPolicyShowsAsync(
            () => settings.ReloadAsync("secrets"),
            async () => await Expect(keys.RotateButton("Standard")).ToBeVisibleAsync());
        await Expect(keys.KeyValue("Standard", "Key")).ToHaveTextAsync(NotBlank());
        await Expect(keys.KeyValue("Standard", "Next rotation")).ToHaveTextAsync(NotBlank());
        await Expect(keys.RowText("HSM", KeyEncryptionKeysCard.NoHsmKey)).ToBeVisibleAsync();
        var before = await keys.CurrentVersionAsync("Standard");

        var dialog = keys.RotateDialog("Standard");
        await keys.RotateButton("Standard").ClickAsync();
        await dialog.ExpectOpenAsync();
        await Expect(dialog.Description).ToHaveTextAsync(KeyEncryptionKeysCard.RotateDialogDescription);

        await dialog.CancelAsync();
        await Expect(keys.CurrentVersion("Standard")).ToHaveTextAsync(before);
        await Expect(App.ShownToasts).ToHaveCountAsync(0);

        await keys.RotateButton("Standard").ClickAsync();
        await dialog.ConfirmAsync("Rotate");
        await Expect(App.Toast("Standard key rotated")).ToBeVisibleAsync();
        await Expect(keys.CurrentVersion("Standard")).Not.ToHaveTextAsync(before);
        var rotated = await keys.CurrentVersionAsync("Standard");

        await Expect(keys.RewrapTitle).ToHaveTextAsync(Target.IsMock ? MockStandardRewrap() : StandardRewrap());
        await Expect(keys.RewrapDescription).ToHaveTextAsync(TargetKeyVersion(rotated));

        await OpenUntilPolicyShowsAsync(
            () => settings.ReloadAsync("secrets"),
            async () => await Expect(keys.CurrentVersion("Standard")).ToHaveTextAsync(rotated));
    }

    // SET-KEY-02: Administrator adds, disables, enables and removes a recipient, and invalid keys are refused.
    // The add form needs a name and a key: a text that is no key and an RSA key of 2048 bits are refused in the form,
    // which keeps the input. An RSA key of 3072 bits as PEM is added with its id; the recipient is disabled and enabled
    // again without a toast, kept when the removal is cancelled, and removed after the confirmation.
    [Test]
    [TestCase(211)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task Recipients_AddDisableRemove_RefusesInvalidKeys()
    {
        var project = await CreateDisposableProjectAsync();
        var name = PlatformData.Title();
        var id = PlatformData.RecipientId();
        const string notAKey = "not a key";

        var settings = new ProjectSettingsPage(App, project);
        var recipients = new RecipientsCard(App, project);
        await settings.OpenAsync("secrets");
        await Expect(recipients.NoRecipients).ToBeVisibleAsync();

        await recipients.OpenFormAsync();
        await recipients.Name.FillAsync(name);
        await Expect(recipients.Id).ToBeVisibleAsync();
        await Expect(recipients.PublicKey).ToBeVisibleAsync();
        await Expect(recipients.AddButton).ToBeDisabledAsync();

        await recipients.PublicKey.FillAsync(notAKey);
        await recipients.AddButton.ClickAsync();
        await Expect(recipients.FormError).ToHaveTextAsync(TargetMessage(
            "The public key is neither PEM nor a JWK object.",
            "The public key is not valid: The public key is neither PEM nor a JWK object."));
        await Expect(recipients.Name).ToHaveValueAsync(name);
        await Expect(recipients.PublicKey).ToHaveValueAsync(notAKey);
        await Expect(recipients.Rows).ToHaveCountAsync(0);

        // A JWK, because the mock takes any PEM as 3072 bits.
        await recipients.PublicKey.FillAsync(PlatformData.RsaPublicJwk(2048));
        await recipients.AddButton.ClickAsync();
        await Expect(recipients.FormError).ToHaveTextAsync(TargetMessage(
            "The recipient key must have at least 3072 bits.",
            "The public key has 2048 bits; at least 3072 are needed."));
        await Expect(recipients.Rows).ToHaveCountAsync(0);

        await recipients.PublicKey.FillAsync(PlatformData.RsaPublicKeyPem());
        await recipients.Id.FillAsync(id);
        await recipients.AddButton.ClickAsync();
        await Expect(App.Toast($"Recipient {id} added")).ToBeVisibleAsync();
        await Expect(App.ToastDescriptionOf($"Recipient {id} added")).ToHaveTextAsync(RecipientsCard.AddedDescription);
        await Expect(recipients.Name).ToHaveCountAsync(0);
        await Expect(recipients.Rows).ToHaveCountAsync(1);
        await Expect(recipients.Cell(id, "Name")).ToHaveTextAsync(name);
        await Expect(recipients.Cell(id, "Id")).ToHaveTextAsync(id);
        await Expect(recipients.Cell(id, "Key")).ToHaveTextAsync("RSA 3072 bits");
        await Expect(recipients.Cell(id, "State")).ToHaveTextAsync("enabled");

        await recipients.SetEnabledAsync(id, enabled: false);
        await recipients.SetEnabledAsync(id, enabled: true);
        await Expect(App.Toast("The recipient was not changed")).ToHaveCountAsync(0);

        var dialog = recipients.RemoveDialog(name);
        await recipients.RowButton(id, "Remove").ClickAsync();
        await dialog.ExpectOpenAsync();
        await Expect(dialog.Description).ToHaveTextAsync(RecipientsCard.RemoveDialogDescription);
        await dialog.CancelAsync();
        await Expect(recipients.Row(id)).ToBeVisibleAsync();

        await recipients.RowButton(id, "Remove").ClickAsync();
        await dialog.ConfirmAsync("Remove");
        await Expect(App.Toast($"Recipient {id} removed")).ToBeVisibleAsync();
        await Expect(recipients.NoRecipients).ToBeVisibleAsync();
        await Expect(recipients.Rows).ToHaveCountAsync(0);
    }

    // SET-KEY-03: Allowing HSM creates the HSM key, which an Administrator rotates.
    // A new project has no HSM key. Allowing HSM (skipped where the deployment has no HSM provider) creates it, with its
    // backend, key, version and next rotation; its own "Rotate now" asks first and gives a new version, kept after a
    // reload with HSM still allowed, and the re-wrap alert targets it.
    [Test]
    [TestCase(215)]
    [UserRole(Role.Owner)]
    [DisposableProject]
    public async Task SecretKeys_AllowHsm_CreatesAndRotatesHsmKey()
    {
        var project = await CreateDisposableProjectAsync();

        var settings = new ProjectSettingsPage(App, project);
        var levels = new AllowedLevelsCard(App, project);
        var keys = new KeyEncryptionKeysCard(App);
        await settings.OpenAsync("secrets");
        await Expect(levels.Allow("HSM")).Not.ToBeCheckedAsync();
        await Expect(levels.Requirement("HSM")).ToHaveTextAsync("Needs: An HSM provider is configured in the deployment.");
        await Expect(keys.RowText("HSM", KeyEncryptionKeysCard.NoHsmKey)).ToBeVisibleAsync();
        await Expect(keys.RotateButton("HSM")).ToHaveCountAsync(0);

        await levels.AllowLevelOrSkipAsync("HSM");
        await Expect(levels.SaveButton).ToBeDisabledAsync();

        // The mock creates a Key Vault Premium key named after the project, at version 0f1e2d3c.
        await Expect(keys.RowText("HSM", Target.IsMock ? MockHsmBackend() : HsmBackend())).ToBeVisibleAsync();
        await Expect(keys.KeyValue("HSM", "Key")).ToHaveTextAsync(Target.IsMock ? new Regex($"^kek-{project.Organization}-{project.Project}$") : NotBlank());
        await Expect(keys.CurrentVersion("HSM")).ToHaveTextAsync(Target.IsMock ? MockHsmVersion() : NotBlank());
        await Expect(keys.KeyValue("HSM", "Next rotation")).ToHaveTextAsync(NotBlank());
        await Expect(keys.RotateButton("HSM")).ToBeVisibleAsync();
        var before = await keys.CurrentVersionAsync("HSM");

        var dialog = keys.RotateDialog("HSM");
        await keys.RotateButton("HSM").ClickAsync();
        await dialog.ExpectOpenAsync();
        await Expect(dialog.Description).ToHaveTextAsync(KeyEncryptionKeysCard.RotateDialogDescription);

        await dialog.ConfirmAsync("Rotate");
        await Expect(App.Toast("HSM key rotated")).ToBeVisibleAsync();
        await Expect(keys.CurrentVersion("HSM")).Not.ToHaveTextAsync(before);
        var rotated = await keys.CurrentVersionAsync("HSM");
        await Expect(keys.RewrapTitle).ToHaveTextAsync(Target.IsMock ? MockHsmRewrap() : HsmRewrap());
        await Expect(keys.RewrapDescription).ToHaveTextAsync(TargetKeyVersion(rotated));

        await OpenUntilPolicyShowsAsync(
            () => settings.ReloadAsync("secrets"),
            async () =>
            {
                await Expect(levels.Allow("HSM")).ToBeCheckedAsync();
                await Expect(keys.CurrentVersion("HSM")).ToHaveTextAsync(rotated);
            });
    }

    // The end of the re-wrap's description: "…; target key version <version>." (no error after it).
    private static Regex TargetKeyVersion(string version)
    {
        return new Regex($"; target key version {Regex.Escape(version)}\\.$");
    }

    [GeneratedRegex(@"\S")]
    private static partial Regex NotBlank();

    // The mock re-wraps at once; a deployed API in its hourly maintenance, so its alert usually shows the re-wrap in
    // progress.
    [GeneratedRegex("^Re-wrap of the Standard values complete$")]
    private static partial Regex MockStandardRewrap();

    [GeneratedRegex("^(Re-wrapping the Standard values|Re-wrap of the Standard values complete)$")]
    private static partial Regex StandardRewrap();

    [GeneratedRegex("^Re-wrap of the Hsm values complete$")]
    private static partial Regex MockHsmRewrap();

    [GeneratedRegex("^(Re-wrapping the Hsm values|Re-wrap of the Hsm values complete)$")]
    private static partial Regex HsmRewrap();

    [GeneratedRegex("^Key Vault Premium$")]
    private static partial Regex MockHsmBackend();

    [GeneratedRegex("^(Key Vault Premium|Managed HSM)$")]
    private static partial Regex HsmBackend();

    [GeneratedRegex("^0f1e2d3c$")]
    private static partial Regex MockHsmVersion();
}
