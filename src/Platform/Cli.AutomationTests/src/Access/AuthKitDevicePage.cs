using System.Text.RegularExpressions;
using _42.Platform.Automation.Shared.Access;
using _42.Testing.UI.Automation.Runtime.Users;
using Microsoft.Playwright;

namespace _42.Platform.Cli.AutomationTests.Access;

// The AuthKit (WorkOS) pages a device code leads to, from verificationUriComplete: possibly the hosted sign-in (e-mail,
// password: AuthKitHostedPage), possibly a page to type the code, then a page that asks to confirm the device, then a
// page that says it is done. WorkOS' exact labels of the device pages were not known when this was written (phase 3 of
// the spec had no AuthKit target), so the steps are found by tolerant role and name patterns and taken in any order, as
// often as they show, until sform ends (it polls the token endpoint and exits once the code is approved or declined):
//   - an Email text box: the credentials, once (AuthKitHostedPage.EnterCredentialsAsync, typed in an untraced context);
//   - a text box named like a code, empty: the user code, then a continue button;
//   - a button that approves (Confirm, Continue, Approve, Allow, Authorize, Yes, Accept) or, to decline, one that
//     declines (Deny, Decline, Cancel, Reject, No): clicked, at most a few times.
// Whatever the page shows when the time is up goes into the failure ("the device page showed: …"), with a screenshot
// whose inputs are masked; the first real run against AuthKit records the labels (see the review of phase 3).
//
// Playwright hands the patterns to the page as JavaScript regular expressions, so they use JavaScript's syntax and no
// option but IgnoreCase (it refuses CultureInvariant and the others).
public static class AuthKitDevicePage
{
    // The name of a button that approves the device.
    public static Regex ApproveButton { get; } = new(@"^\s*(Confirm|Continue(?!\s+with)|Approve|Allow|Authori[sz]e|Yes|Accept)\b", RegexOptions.IgnoreCase);

    // The name of a button that declines it.
    public static Regex DeclineButton { get; } = new(@"^\s*(Deny|Decline|Cancel|Reject|No|Don't allow)\b", RegexOptions.IgnoreCase);

    // The name of a text box for the user code.
    public static Regex CodeField { get; } = new(@"code", RegexOptions.IgnoreCase);

    // Works through the pages until done() (sform ended) or the deadline; returns what it did, for the failure message.
    public static async Task<IReadOnlyList<string>> CompleteAsync(
        IPage page,
        DeviceCodePrompt prompt,
        TestUser user,
        bool decline,
        Func<bool> done,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(done);

        var steps = new List<string>();
        var deadline = DateTimeOffset.UtcNow + timeout;
        var credentialsEntered = false;
        var codeEntered = false;
        var clicks = 0;
        var lastUrl = string.Empty;

        await page.GotoAsync(prompt.Address.AbsoluteUri, new PageGotoOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        steps.Add("opened the verification address");

        while (!done() && DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.Equals(page.Url, lastUrl, StringComparison.Ordinal))
            {
                lastUrl = page.Url;
                clicks = Math.Min(clicks, 2);
            }

            var email = page.GetByRole(AriaRole.Textbox, new() { Name = "Email" });
            if (!credentialsEntered && await IsVisibleAsync(email))
            {
                await AuthKitHostedPage.EnterCredentialsAsync(page, user.Email, AuthKitHostedPage.PasswordOf(user));
                credentialsEntered = true;
                steps.Add("entered the credentials on the hosted sign-in page");
                await SettleAsync(page);
                continue;
            }

            var code = page.GetByRole(AriaRole.Textbox, new() { NameRegex = CodeField });
            if (!codeEntered && await IsVisibleAsync(code) && string.IsNullOrEmpty(await code.First.InputValueAsync()))
            {
                await code.First.FillAsync(prompt.UserCode);
                codeEntered = true;
                steps.Add("typed the user code");
                var next = page.GetByRole(AriaRole.Button, new() { NameRegex = ApproveButton });
                if (await IsVisibleAsync(next))
                {
                    await next.First.ClickAsync();
                    steps.Add($"clicked '{await NameOfAsync(next.First)}' after the code");
                }

                await SettleAsync(page);
                continue;
            }

            var button = page.GetByRole(AriaRole.Button, new() { NameRegex = decline ? DeclineButton : ApproveButton });
            if (clicks < 4 && await IsVisibleAsync(button) && await button.First.IsEnabledAsync())
            {
                var name = await NameOfAsync(button.First);
                await button.First.ClickAsync();
                clicks++;
                steps.Add($"clicked '{name}'");
                await SettleAsync(page);
                continue;
            }

            await Task.Delay(500, cancellationToken);
        }

        return steps;
    }

    // The visible text of the page, for a failure; empty when the page cannot be read.
    public static async Task<string> TextOfAsync(IPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        try
        {
            var text = await page.Locator("body").InnerTextAsync(new() { Timeout = 5_000 });
            return Regex.Replace(text, @"\s+", " ").Trim();
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            return string.Empty;
        }
    }

    private static async Task<bool> IsVisibleAsync(ILocator locator)
    {
        try
        {
            return await locator.CountAsync() > 0 && await locator.First.IsVisibleAsync();
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            // the page navigated meanwhile
            return false;
        }
    }

    private static async Task<string> NameOfAsync(ILocator button)
    {
        try
        {
            return (await button.InnerTextAsync(new() { Timeout = 2_000 })).Trim();
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            return "a button";
        }
    }

    private static async Task SettleAsync(IPage page)
    {
        try
        {
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new() { Timeout = 10_000 });
        }
        catch (TimeoutException)
        {
            // a page that keeps loading is looked at again anyway
        }

        await Task.Delay(500);
    }
}
