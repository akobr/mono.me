using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// A "Date format" select (a USelect, so a combobox with options): the project's on Settings (card "Dates") and the
// personal one of Preferences on the account page, which starts with "Project default". Each format's option is named
// by today's date in the format and the pattern, "2026-11-02 (yyyy-mm-dd)", in the order yyyy-mm-dd (the default),
// dd.mm.yyyy, dd/mm/yyyy, mm/dd/yyyy and d mmm yyyy (utils/dates.ts). The choice is kept in this browser and applies at
// once, without a Save and without a toast: every date of the app follows it, a date and time as "<date> <HH:mm>
// <zone>" ("2026-11-02 10:15 GMT+1"). The effective format is the personal one, else the project's, else yyyy-mm-dd.
public sealed partial class DateFormatSelect(App app, ILocator combobox)
{
    public const string IsoFormat = "yyyy-mm-dd";
    public const string DottedFormat = "dd.mm.yyyy";
    public const string SlashedFormat = "dd/mm/yyyy";
    public const string UsFormat = "mm/dd/yyyy";
    public const string MonthNameFormat = "d mmm yyyy";

    // The item of Preferences that follows the project's format.
    public const string ProjectDefault = "Project default";

    private static readonly string[] MonthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    public ILocator Combobox => combobox;

    public ILocator ProjectDefaultOption => app.Page.GetByRole(AriaRole.Option, new() { Name = ProjectDefault, Exact = true });

    // The option of a format (its pattern, e.g. "dd.mm.yyyy"), whatever today's date before it. Playwright writes the
    // regex into its selector between slashes, so the pattern's '/' is escaped too.
    public ILocator Option(string format)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(format);

        var pattern = Regex.Escape(format).Replace("/", @"\/", StringComparison.Ordinal);
        return app.Page.GetByRole(AriaRole.Option, new() { NameRegex = new Regex($@"\({pattern}\)$") });
    }

    // A date in the format, as utils/dates.ts writes it.
    public static string FormatDate(DateOnly date, string format)
    {
        var day = date.Day.ToString("00", CultureInfo.InvariantCulture);
        var month = date.Month.ToString("00", CultureInfo.InvariantCulture);
        var year = date.Year.ToString("0000", CultureInfo.InvariantCulture);
        return format switch
        {
            IsoFormat => $"{year}-{month}-{day}",
            DottedFormat => $"{day}.{month}.{year}",
            SlashedFormat => $"{day}/{month}/{year}",
            UsFormat => $"{month}/{day}/{year}",
            MonthNameFormat => $"{date.Day.ToString(CultureInfo.InvariantCulture)} {MonthNames[date.Month - 1]} {year}",
            _ => throw new ArgumentException($"'{format}' is not one of the app's date formats.", nameof(format)),
        };
    }

    // The same instant as a date and time shown in another format: "2026-11-02 10:15 GMT+1" (yyyy-mm-dd) is
    // "02.11.2026 10:15 GMT+1" in dd.mm.yyyy. Only the date changes; the time and the zone stay.
    public static string ReformatIsoDateTime(string shown, string format)
    {
        ArgumentNullException.ThrowIfNull(shown);

        var match = IsoDateTime().Match(shown.Trim());
        if (!match.Success)
        {
            throw new ArgumentException($"'{shown}' is not a date and time shown as yyyy-mm-dd HH:mm <zone>.", nameof(shown));
        }

        var date = DateOnly.ParseExact(match.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        return $"{FormatDate(date, format)} {match.Groups["rest"].Value}";
    }

    // Today in the browser's time zone, the date the options show.
    public async Task<DateOnly> TodayAsync()
    {
        var parts = await app.Page.EvaluateAsync<int[]>("() => { const now = new Date(); return [now.getFullYear(), now.getMonth() + 1, now.getDate()]; }");
        return new DateOnly(parts[0], parts[1], parts[2]);
    }

    // Waits until the select shows the format's item: today's date in the format and " (<pattern>)".
    public async Task ExpectFormatAsync(string format)
    {
        var today = await TodayAsync();
        await Expect(combobox).ToHaveTextAsync($"{FormatDate(today, format)} ({format})");
    }

    // Picks the format by its pattern and waits until the select shows it.
    public async Task SelectAsync(string format)
    {
        await combobox.ClickAsync();
        await Option(format).ClickAsync();
        await ExpectFormatAsync(format);
    }

    // Picks "Project default" of Preferences and waits until the select shows it.
    public async Task SelectProjectDefaultAsync()
    {
        await combobox.ClickAsync();
        await ProjectDefaultOption.ClickAsync();
        await Expect(combobox).ToHaveTextAsync(ProjectDefault);
    }

    // A date and time shown as yyyy-mm-dd HH:mm and the zone: "2026-11-02 10:15 GMT+1".
    [GeneratedRegex(@"^(?<date>\d{4}-\d{2}-\d{2}) (?<rest>\d{2}:\d{2} \S+)$")]
    private static partial Regex IsoDateTime();
}
