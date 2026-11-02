using System.Globalization;

namespace _42.Platform.UI.Admin.AutomationTests.Support;

// The calendar days of one reading of the UTC clock, for the date inputs of Monitor (the usage report's range and the
// audit trail's From and To are UTC days) and their texts: Iso(day) is yyyy-MM-dd in the invariant culture, as the
// inputs take and the report shows them. Read the clock once (Now) and derive every day from it, so the days agree
// even when midnight passes during the test.
public sealed record UtcDates(DateOnly Today)
{
    public DateOnly Yesterday => Today.AddDays(-1);

    public DateOnly Tomorrow => Today.AddDays(1);

    public DateOnly FirstOfMonth => new(Today.Year, Today.Month, 1);

    public DateOnly FirstOfPreviousMonth => FirstOfMonth.AddMonths(-1);

    public DateOnly LastOfPreviousMonth => FirstOfMonth.AddDays(-1);

    public static UtcDates Now()
    {
        return new UtcDates(DateOnly.FromDateTime(DateTime.UtcNow));
    }

    public static string Iso(DateOnly day)
    {
        return day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public static DateOnly ParseIso(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return DateOnly.ParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
