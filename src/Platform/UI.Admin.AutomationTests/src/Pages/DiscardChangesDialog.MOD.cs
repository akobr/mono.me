using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Modeling section's member of DiscardChangesDialog: a navigation that may ask or not, e.g. leaving a deleted schema
// or template, whose editor is dirty with a known bug of the admin UI and may be clean without it.
public sealed partial class DiscardChangesDialog
{
    // How long DiscardIfAskedAsync waits for the dialog; it opens right after the click that navigates.
    public const int AskTimeoutMs = 3_000;

    // After the click that navigates: discards when the dialog opens within timeoutMs and returns whether it asked.
    // Without the dialog the navigation goes on by itself; the caller asserts where it ends.
    public async Task<bool> DiscardIfAskedAsync(int timeoutMs = AskTimeoutMs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMs, 1);

        try
        {
            await Root.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = timeoutMs });
        }
        catch (TimeoutException)
        {
            return false;
        }

        await DiscardAsync();
        return true;
    }
}
