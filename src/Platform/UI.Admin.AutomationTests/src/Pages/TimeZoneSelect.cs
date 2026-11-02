using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The time zone picker (components/annotations/TimeZoneSelect.vue): a button that shows the zone ("UTC", "Asia/Tokyo")
// and opens a searchable, virtualized list: UTC first, then every IANA zone of the browser, each option named by the
// zone and its current offset ("Asia/Tokyo GMT+9"). Only the options in view exist, so a zone is searched first. The
// button is "Time zone" in the New annotation dialog and an annotation's form, "Default time zone" in the project's
// Settings.
public sealed partial class TimeZoneSelect(App app, ILocator button)
{
    public ILocator Button => button;

    public ILocator Search => app.Page.GetByPlaceholder("Search time zones…", new() { Exact = true });

    // The option of the zone (an IANA name such as "Asia/Tokyo", or "UTC"), whatever its offset. Playwright writes the
    // regex into its selector between slashes, so the zone's '/' is escaped too.
    public ILocator Option(string zone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zone);

        var name = Regex.Escape(zone).Replace("/", @"\/", StringComparison.Ordinal);
        return app.Page.GetByRole(AriaRole.Option, new() { NameRegex = new Regex($@"^{name}(?:\s|$)") });
    }

    // Picks the zone and waits until the button shows it.
    public async Task SelectAsync(string zone)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zone);

        await button.ClickAsync();
        await Search.FillAsync(zone);
        await Option(zone).First.ClickAsync();
        await Expect(button).ToContainTextAsync(zone);
    }
}
