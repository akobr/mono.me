using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// Monitoring's members of the settings: the secrets changes whose audit events MON-AUD-03 follows, in a disposable
// project. The mTLS rule's switch in Allowed levels ("Require mTLS for secret delivery", on in a new project; turning
// it off asks "Deliver secrets to machines without mTLS?" on "Save secrets policy"), and the Standard key of
// "Key-encryption keys": its "Current version" and "Rotate now" (which asks "Rotate the Standard key now?"). The card
// lists Standard before HSM, and a new project has no HSM key; on a deployed target the Standard key only exists after
// the first secret (or the daily check), so its row has no version and no "Rotate now" before.
public sealed partial class ProjectSettingsPage
{
    // The switch has aria-label "Machines", its field's label, which wins over its own label element "Require mTLS for
    // secret delivery" (so neither GetByLabel nor its role name find it by that text). It is the switch of the Nuxt UI
    // root around that label.
    public ILocator SecretDeliveryMtlsSwitch => AllowedLevelsCard
        .GetByText("Require mTLS for secret delivery", new() { Exact = true })
        .Locator("xpath=ancestor::*[@data-slot='root'][1]")
        .GetByRole(AriaRole.Switch);

    public ILocator KeyEncryptionKeysOfSecrets => Card("Key-encryption keys", 3);

    // The Standard row's version: the first "Current version" of the card.
    public ILocator StandardKeyCurrentVersion => KeyEncryptionKeysOfSecrets.Locator("dt:text-is(\"Current version\") + dd").First;

    // The Standard row's "Rotate now": the first of the card (the HSM row's comes second).
    public ILocator RotateStandardKeyButton => KeyEncryptionKeysOfSecrets.GetByRole(AriaRole.Button, new() { Name = "Rotate now", Exact = true }).First;
}
