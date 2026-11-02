using System.Text.RegularExpressions;
using _42.Platform.UI.Admin.AutomationTests.Pages;
using _42.Platform.UI.Admin.AutomationTests.Support;
using _42.Testing.UI.Automation;
using _42.Testing.UI.Automation.Runtime.TUnit;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Tests.Monitoring;

// Monitor Page: the Monitor of a project with its tabs Usage (the default) and Audit (?tab=audit), who may see it, the
// usage report's date range, and the old /usage and /audit links. Nothing is created, and no meter value is asserted:
// the values depend on the project and, on a deployed target, on the aggregation.
[Area(Area.Monitoring)]
public sealed partial class MonitorTests : PlatformTest
{
    // MON-PAG-01: Monitor opens on Usage and switches to Audit through the tabs.
    // Monitor opens on Usage without a tab in the URL, with this month's report (From the first day of the month, To
    // today, in UTC); the tab Audit adds ?tab=audit and shows the audit's filters, its counter and its events or the
    // empty state, and stays selected after a reload, which clears a typed Key filter; the tab Usage drops ?tab= again.
    [Test]
    [Elementary]
    [TestCase(7)]
    [UserRole(Role.Admin)]
    public async Task Monitor_SwitchTabs_ShowsUsageAndAudit()
    {
        // Read right before the navigation: near midnight UTC the page may already show the next day.
        var dates = UtcDates.Now();

        var monitor = new MonitorPage(App, Routes);
        await monitor.OpenAsync();
        await App.ExpectAtAsync(Routes.Monitor());
        await App.ExpectQueryAsync("tab", null);

        var usage = monitor.Usage;
        var (from, to) = await ExpectCurrentMonthAsync(usage, dates);
        await Expect(usage.TotalsHeading(from, to)).ToBeVisibleAsync();
        await Expect(usage.ChargesHeading).ToBeVisibleAsync();
        await Expect(usage.PerDayHeading).ToBeVisibleAsync();

        await monitor.SelectAuditAsync();
        var audit = monitor.Audit;
        await Expect(audit.ViewFilter).ToBeVisibleAsync();
        await Expect(audit.KeyFilter).ToBeVisibleAsync();
        await Expect(audit.OperationFilter).ToContainTextAsync(AuditTrail.EveryOperation);
        await Expect(audit.From).ToHaveValueAsync(string.Empty);
        await Expect(audit.To).ToHaveValueAsync(string.Empty);
        await Expect(audit.Table.Or(audit.NoEvents)).ToBeVisibleAsync();
        await Expect(audit.Counter).ToBeVisibleAsync();

        // A key without events in Key filter, so that the reload has a filter to clear.
        await audit.KeyFilter.FillAsync(PlatformData.KeyValueKey());
        await Expect(audit.NoEvents).ToBeVisibleAsync();
        await Expect(audit.Counter).ToHaveTextAsync(AuditTrail.CounterOf(0));

        await Page.ReloadAsync();
        await App.WaitForReadyAsync();
        await monitor.ExpectSelectedAsync(MonitorPage.AuditTabName);
        await Expect(audit.KeyFilter).ToBeVisibleAsync();
        await Expect(audit.KeyFilter).ToHaveValueAsync(string.Empty);

        await monitor.SelectUsageAsync();
        await Expect(usage.ChargesHeading).ToBeVisibleAsync();
    }

    // MON-PAG-02: Monitor is for Administrators only.
    // A Reader of the project gets "Administrators only" on Monitor, without the tabs or the usage's date inputs, and
    // the old link /audit leads to the same empty state at monitor?tab=audit, without the audit's filters.
    [Test]
    [TestCase(9)]
    [UserRole(Role.Reader)]
    public async Task Monitor_OpenAsReader_ShowsAdministratorsOnly()
    {
        var monitor = new MonitorPage(App, Routes);
        await App.GotoAsync(Routes.Monitor());
        await App.WaitForReadyAsync();
        await Expect(monitor.AdministratorsOnly).ToBeVisibleAsync();
        await Expect(monitor.AdministratorsOnlyText).ToBeVisibleAsync();

        // Only once the empty state shows, so the absence cannot pass on a page that has not rendered yet.
        await Expect(monitor.Tabs).ToHaveCountAsync(0);
        await Expect(monitor.Usage.From).ToHaveCountAsync(0);

        await App.GotoAsync(Routes.OldProjectPage("audit"));
        await App.ExpectAtAsync(Routes.Monitor());
        await App.ExpectQueryAsync("tab", "audit");
        await App.WaitForReadyAsync();
        await Expect(monitor.AdministratorsOnly).ToBeVisibleAsync();
        await Expect(monitor.Audit.KeyFilter).ToHaveCountAsync(0);
    }

    // MON-PAG-03: The usage report follows the chosen date range and refuses a reversed one.
    // The report of the previous calendar month (From its first day, To its last) is titled with that range and shows
    // meters or "No usage recorded in this range.", a chart of at most that month's days within the range or "No days
    // with data.", and dollar charges or the note that no prices are configured; a To before From is refused with an
    // error alert and Retry instead of the report, and the range's report is back once To is valid again.
    [Test]
    [TestCase(10)]
    [UserRole(Role.Admin)]
    public async Task MonitorUsage_ChangeRange_ShowsReportOfRange()
    {
        var dates = UtcDates.Now();
        var first = dates.FirstOfPreviousMonth;
        var last = dates.LastOfPreviousMonth;
        var (from, to, before) = (UtcDates.Iso(first), UtcDates.Iso(last), UtcDates.Iso(first.AddDays(-1)));

        var monitor = new MonitorPage(App, Routes);
        await monitor.OpenAsync();
        var usage = monitor.Usage;
        var (monthFrom, today) = await ExpectCurrentMonthAsync(usage, dates);
        await Expect(usage.TotalsHeading(monthFrom, today)).ToBeVisibleAsync();

        await usage.SetRangeAsync(from, to);

        // Branch on what the report shows, never on the target: the meters are not under the test's control.
        await Expect(usage.MeterLabels.First.Or(usage.NoUsage)).ToBeVisibleAsync();
        if (!await usage.NoUsage.IsVisibleAsync())
        {
            var meters = await usage.MeterLabels.CountAsync();
            await Expect(usage.MeterValues).ToHaveCountAsync(meters);
            for (var index = 0; index < meters; index++)
            {
                await Expect(usage.MeterLabels.Nth(index)).Not.ToBeEmptyAsync();
                await Expect(usage.MeterValues.Nth(index)).ToHaveTextAsync(MeterNumber());
            }
        }

        await Expect(usage.Chart.Or(usage.NoDays)).ToBeVisibleAsync();
        if (await usage.Chart.IsVisibleAsync())
        {
            var days = await usage.ChartDaysAsync();
            await Assert.That(days).IsGreaterThanOrEqualTo(1);
            await Assert.That(days).IsLessThanOrEqualTo(DateTime.DaysInMonth(first.Year, first.Month));

            await Expect(usage.ChartDates).ToHaveCountAsync(2);
            var firstDay = UtcDates.ParseIso(await usage.ChartDates.First.InnerTextAsync());
            var lastDay = UtcDates.ParseIso(await usage.ChartDates.Last.InnerTextAsync());
            await Assert.That(firstDay).IsGreaterThanOrEqualTo(first);
            await Assert.That(lastDay).IsGreaterThanOrEqualTo(firstDay);
            await Assert.That(lastDay).IsLessThanOrEqualTo(last);
        }

        await Expect(usage.ChargesTotal.Or(usage.NoPrices)).ToBeVisibleAsync();
        if (await usage.ChargesTotal.IsVisibleAsync())
        {
            await Expect(usage.ChargesNote).ToBeVisibleAsync();
            var amounts = await usage.ChargeAmounts.CountAsync();
            await Assert.That(amounts).IsGreaterThanOrEqualTo(2);
            for (var index = 0; index < amounts; index++)
            {
                await Expect(usage.ChargeAmounts.Nth(index)).ToHaveTextAsync(DollarAmount());
            }
        }

        await usage.To.FillAsync(before);
        await Expect(usage.ErrorAlert).ToBeVisibleAsync();
        await Expect(usage.ErrorAlert).ToContainTextAsync(UsageReport.ReversedRangeMessage);
        await Expect(usage.RetryButton).ToBeVisibleAsync();
        await Expect(usage.AnyTotalsHeading).ToHaveCountAsync(0);

        await usage.To.FillAsync(to);
        await Expect(usage.TotalsHeading(from, to)).ToBeVisibleAsync();
        await Expect(usage.ErrorAlert).ToHaveCountAsync(0);
    }

    // MON-PAG-04: Old Usage and Audit links open the Monitor tabs.
    // The old link /usage opens Monitor on Usage without a tab, the old link /audit opens it on Audit (?tab=audit), and
    // an unknown tab (?tab=summary) opens Usage.
    [Test]
    [TestCase(11)]
    [UserRole(Role.Admin)]
    public async Task Monitor_OpenOldLinks_OpensTheirTabs()
    {
        var monitor = new MonitorPage(App, Routes);

        await App.GotoAsync(Routes.OldProjectPage("usage"));
        await App.ExpectAtAsync(Routes.Monitor());
        await App.ExpectQueryAsync("tab", null);
        await App.WaitForReadyAsync();
        await monitor.ExpectSelectedAsync(MonitorPage.UsageTabName);
        await Expect(monitor.Usage.ChargesHeading).ToBeVisibleAsync();

        await App.GotoAsync(Routes.OldProjectPage("audit"));
        await App.ExpectAtAsync(Routes.Monitor());
        await App.ExpectQueryAsync("tab", "audit");
        await App.WaitForReadyAsync();
        await monitor.ExpectSelectedAsync(MonitorPage.AuditTabName);
        await Expect(monitor.Audit.KeyFilter).ToBeVisibleAsync();

        // The page keeps an unknown tab in the query and shows Usage.
        await App.GotoAsync(Routes.Monitor("summary"));
        await App.WaitForReadyAsync();
        await monitor.ExpectSelectedAsync(MonitorPage.UsageTabName);
        await Expect(monitor.Usage.ChargesHeading).ToBeVisibleAsync();
    }

    // The default range of the usage report: To is today in UTC, or tomorrow only when midnight UTC has passed since
    // the clock was read (the clock then says so too), and From the first day of To's month. Returns both as the
    // inputs show them.
    private static async Task<(string From, string To)> ExpectCurrentMonthAsync(UsageReport usage, UtcDates dates)
    {
        await Expect(usage.To).ToHaveValueAsync(IsoDay());
        var to = UtcDates.ParseIso(await usage.To.InputValueAsync());
        if (to != dates.Today)
        {
            await Assert.That(to).IsEqualTo(dates.Tomorrow);
            await Assert.That(UtcDates.Now().Today).IsEqualTo(dates.Tomorrow);
        }

        var from = UtcDates.Iso(new DateOnly(to.Year, to.Month, 1));
        await Expect(usage.From).ToHaveValueAsync(from);
        return (from, UtcDates.Iso(to));
    }

    // A day as the date inputs hold it, yyyy-MM-dd.
    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}$")]
    private static partial Regex IsoDay();

    // A meter's number as Intl.NumberFormat writes it (grouped, at most 2 decimals; the browser's locale).
    [GeneratedRegex(@"^\d[\d,.  ]*$")]
    private static partial Regex MeterNumber();

    // A charge in US dollars, e.g. "$0.10" or "$1,234.50".
    [GeneratedRegex(@"^\$\d[\d,]*(\.\d{2})?$")]
    private static partial Regex DollarAmount();
}
