using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Access section's part of /account: the profile card, top to bottom the signed-in user's name and e-mail (of the
// sign-in, AuthKit's), and the account id with its "Copy to clipboard" (no toast: the id is no secret), next to "Sign
// out".
public sealed partial class AccountPage
{
    // The profile card, the copy button's nearest Nuxt UI card (data-slot="root").
    public ILocator ProfileCard => CopyAccountIdButton.Locator("xpath=ancestor::*[@data-slot='root'][1]");

    // The first line of the profile: the user's name.
    public ILocator ProfileName => ProfileCard.Locator("p").Nth(0);

    // The second line of the profile: the user's e-mail.
    public ILocator ProfileEmail => ProfileCard.Locator("p").Nth(1);

    // The account id: the line of its copy button.
    public ILocator AccountId => CopyAccountIdButton.Locator("xpath=..");

    // The account id as the card shows it.
    public async Task<string> AccountIdAsync()
    {
        return (await AccountId.InnerTextAsync()).Trim();
    }
}
