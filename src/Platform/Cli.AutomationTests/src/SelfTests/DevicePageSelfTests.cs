using _42.Platform.Cli.AutomationTests.Access;
using _42.Testing.UI.Automation.Runtime.Users;
using Microsoft.Playwright;

namespace _42.Platform.Cli.AutomationTests.SelfTests;

// The steps on the device page (AuthKitDevicePage) against a stand-in of AuthKit's pages, served by Playwright's routing
// in a local headless Chromium: the e-mail step, the password step (AuthKitHostedPage), then a page that asks to confirm
// the device, and the end. The real labels of AuthKit's device pages are verified by the first run against a target
// (the review of phase 3); this checks the order-free step machine, the approval and the decline. Skipped when
// Playwright's Chromium is not installed (pwsh bin/<configuration>/net10.0/playwright.ps1 install chromium).
[Category(CliCategories.SelfTest)]
public sealed class DevicePageSelfTests
{
    private const string Address = "https://auth.example.test/device?user_code=ABCD-EFGH";

    // The stand-in: three steps in one page, each shown after the button of the step before.
    private const string Html = """
        <!doctype html>
        <html><body>
        <form id="email-step" onsubmit="event.preventDefault(); captured.email = email.value; this.hidden = true; document.getElementById('password-step').hidden = false;">
          <label for="email">Email</label><input id="email" type="email">
          <button type="submit">Continue</button>
        </form>
        <form id="password-step" hidden onsubmit="event.preventDefault(); captured.password = pw.value; this.hidden = true; document.getElementById('confirm-step').hidden = false;">
          <label for="pw">Password</label><input id="pw" type="password">
          <button type="submit">Sign in</button>
        </form>
        <div id="confirm-step" hidden>
          <p>Confirm that the code ABCD-EFGH is shown on your device.</p>
          <button onclick="finish('approved')">Confirm</button>
          <button onclick="finish('declined')">Cancel</button>
        </div>
        <p id="done" hidden></p>
        <script>
          window.captured = {};
          function finish(outcome) {
            document.getElementById('confirm-step').hidden = true;
            const done = document.getElementById('done');
            done.textContent = 'Device ' + outcome + '.';
            done.hidden = false;
            window.reportOutcome(outcome);
          }
        </script>
        </body></html>
        """;

    [Test]
    public async Task DevicePage_SignsInAndConfirms()
    {
        var (outcome, steps, captured) = await RunAsync(decline: false);

        await Assert.That(outcome).IsEqualTo("approved");
        await Assert.That(steps).Contains("entered the credentials on the hosted sign-in page");
        await Assert.That(steps).Contains("clicked 'Confirm'");
        await Assert.That(captured).IsEqualTo("cli-test-reader-1@42for.software|not-a-real-password");
    }

    // SGN-DEV-05 on the stand-in: the decline clicks the button that declines.
    [Test]
    public async Task DevicePage_Declines()
    {
        var (outcome, steps, _) = await RunAsync(decline: true);

        await Assert.That(outcome).IsEqualTo("declined");
        await Assert.That(steps).Contains("clicked 'Cancel'");
    }

    private static async Task<(string? Outcome, IReadOnlyList<string> Steps, string Captured)> RunAsync(bool decline)
    {
        using var playwright = await Playwright.CreateAsync();
        IBrowser browser;
        try
        {
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }
        catch (PlaywrightException exception)
        {
            Skip.Test($"Playwright's Chromium is not installed ({exception.Message.Split('\n')[0]}); pwsh bin/<configuration>/net10.0/playwright.ps1 install chromium.");
            throw;
        }

        await using (browser)
        {
            var context = await browser.NewContextAsync();
            var page = await context.NewPageAsync();
            page.SetDefaultTimeout(10_000);
            string? outcome = null;
            await page.ExposeFunctionAsync("reportOutcome", (string value) => outcome = value);
            await page.RouteAsync("https://auth.example.test/**", route => route.FulfillAsync(new RouteFulfillOptions { ContentType = "text/html", Body = Html }));

            var prompt = DeviceCodePrompt.Parse($"{{\"userCode\":\"ABCD-EFGH\",\"verificationUri\":\"https://auth.example.test/device\",\"verificationUriComplete\":\"{Address}\",\"expiresIn\":300}}");
            var user = new TestUser(6, "cli-test-reader-1@42for.software", "not-a-real-password", "CLI Reader1", ["Reader"]);
            var steps = await AuthKitDevicePage.CompleteAsync(page, prompt, user, decline, () => outcome is not null, TimeSpan.FromSeconds(30), CancellationToken.None);
            var captured = await page.EvaluateAsync<string>("() => (captured.email ?? '') + '|' + (captured.password ?? '')");
            return (outcome, steps, captured);
        }
    }
}
