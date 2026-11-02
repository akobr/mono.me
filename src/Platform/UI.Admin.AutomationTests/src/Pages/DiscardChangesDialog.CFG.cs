using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Configurations section's part of "Discard changes?": a navigation that may or may not ask.
public sealed partial class DiscardChangesDialog
{
    // After a click that navigates: waits until either this dialog or the outcome of the navigation shows, and discards
    // the draft only if the dialog asked, so the navigation goes on. The outcome must be something that exists only
    // after the navigation (the next tab's content, a detail gone), never something already shown before the click;
    // while the modal is open the page is aria-hidden, so a role locator of the page cannot match then.
    public async Task DiscardIfShownAsync(ILocator outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        await Expect(Root.Or(outcome).First).ToBeVisibleAsync();
        if (await Root.IsVisibleAsync())
        {
            await DiscardAsync();
        }
    }
}
