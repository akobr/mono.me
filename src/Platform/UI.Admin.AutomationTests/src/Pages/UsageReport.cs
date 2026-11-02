using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Usage tab of Monitor (components/monitor/UsageReport.vue): the native date inputs "From" (the first day of the
// current month in UTC) and "To" (today in UTC), yyyy-MM-dd, and for that range three cards (h2): "Totals <From> to
// <To>" (the meters with a non-zero total, a term and its number each, or "No usage recorded in this range."),
// "Estimated charges" (dollar rows, "Total" and the note, or "The deployment has no prices configured; the meters are
// informational.") and "Per day" (the chart "Daily reads and keys for <n> days" with its first and last date under it,
// or "No days with data."). Every change of a date refetches at once (no debounce); a refused range (To before From)
// shows a compact error alert instead of the cards: "Something went wrong" with the message and "Retry" (Nuxt UI's
// alert has no role "alert", so it is found by its title). The values depend on the project and, on a deployed target,
// on the aggregation (counters every 60 s, key gauges daily), so a test checks the range and the shape only.
public sealed partial class UsageReport(App app)
{
    public const string ErrorTitle = "Something went wrong";

    public const string ReversedRangeMessage = "The end of the range is before its start.";

    public ILocator From => app.Page.GetByLabel("From", new() { Exact = true });

    public ILocator To => app.Page.GetByLabel("To", new() { Exact = true });

    // The heading of the totals of any range, e.g. while the report is shown at all.
    public ILocator AnyTotalsHeading => app.Page.GetByRole(AriaRole.Heading, new() { NameRegex = TotalsName(), Level = 2 });

    public ILocator TotalsCard => CardOf(AnyTotalsHeading);

    // The meters of the totals: a term (the meter's label, e.g. "Standard keys (key-months)", "API reads") and a
    // definition (its number) each.
    public ILocator MeterLabels => TotalsCard.Locator("dt");

    public ILocator MeterValues => TotalsCard.Locator("dd");

    public ILocator NoUsage => app.Page.GetByText("No usage recorded in this range.", new() { Exact = true });

    public ILocator ChargesHeading => app.Page.GetByRole(AriaRole.Heading, new() { Name = "Estimated charges", Exact = true, Level = 2 });

    public ILocator ChargesCard => CardOf(ChargesHeading);

    // The dollar amounts of the charges, the Total's last.
    public ILocator ChargeAmounts => ChargesCard.Locator("dd");

    public ILocator ChargesTotal => ChargesCard.GetByText("Total", new() { Exact = true });

    public ILocator ChargesNote => ChargesCard.GetByText("An estimate from the list prices; the invoice is authoritative.", new() { Exact = true });

    public ILocator NoPrices => app.Page.GetByText("The deployment has no prices configured; the meters are informational.", new() { Exact = true });

    public ILocator PerDayHeading => app.Page.GetByRole(AriaRole.Heading, new() { Name = "Per day", Exact = true, Level = 2 });

    // The daily chart, named "Daily reads and keys for <n> days" (n: the days of the report).
    public ILocator Chart => app.Page.GetByRole(AriaRole.Img, new() { NameRegex = ChartName() });

    // The first and the last date of the chart, in the row under it.
    public ILocator ChartDates => Chart.Locator("xpath=following-sibling::div[1]/span");

    public ILocator NoDays => app.Page.GetByText("No days with data.", new() { Exact = true });

    // The compact error alert of a refused report: its title's nearest Nuxt UI root, with the message and "Retry".
    public ILocator ErrorAlert => app.Page.GetByText(ErrorTitle, new() { Exact = true }).Locator("xpath=ancestor::*[@data-slot='root'][1]");

    public ILocator RetryButton => ErrorAlert.GetByRole(AriaRole.Button, new() { Name = "Retry", Exact = true });

    // The heading of the totals of the range, as the report names it: "Totals <from> to <to>", both yyyy-MM-dd.
    public ILocator TotalsHeading(string from, string to)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(from);
        ArgumentException.ThrowIfNullOrWhiteSpace(to);

        return app.Page.GetByRole(AriaRole.Heading, new() { Name = $"Totals {from} to {to}", Exact = true, Level = 2 });
    }

    // The days of the chart, from its name "Daily reads and keys for <n> days".
    public async Task<int> ChartDaysAsync()
    {
        var name = await Chart.GetAttributeAsync("aria-label") ?? string.Empty;
        var match = ChartName().Match(name);
        return match.Success
            ? int.Parse(match.Groups["days"].Value, CultureInfo.InvariantCulture)
            : throw new InvalidOperationException($"The chart is named '{name}', not 'Daily reads and keys for <n> days'.");
    }

    // Sets the range, From first (To first could reverse the range for a moment), and waits for the totals of it.
    public async Task SetRangeAsync(string from, string to)
    {
        await From.FillAsync(from);
        await To.FillAsync(to);
        await Expect(TotalsHeading(from, to)).ToBeVisibleAsync();
    }

    // A card of the report by its heading: Nuxt UI's card is the heading's nearest data-slot="root".
    private static ILocator CardOf(ILocator heading)
    {
        return heading.Locator("xpath=ancestor::*[@data-slot='root'][1]");
    }

    [GeneratedRegex("^Totals ")]
    private static partial Regex TotalsName();

    [GeneratedRegex(@"^Daily reads and keys for (?<days>\d+) days$")]
    private static partial Regex ChartName();
}
