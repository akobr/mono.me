using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Audit tab of Monitor (components/monitor/AuditTrail.vue, ?tab=audit): the project's secret events, newest first.
// Its toolbar has the textboxes "View filter" and "Key filter", the select "Operation filter" ("Every operation" or one
// operation: Reveal, Export, Create, PolicyChange, Rotate, RecipientAdded, …), the date inputs "From" and "To" (UTC
// days; To includes its whole day) and the counter "<n> event(s)" of the listed events. Every filter change refetches at
// once (trimmed); the filters are not kept in the URL, so a reload clears them. Below it is the table (columns Time,
// Operation with its detail as a second line, View, Key, Version "v<n>", Level, Actor, Channel, Count, Correlation: the
// first 8 characters and "…" with "Copy to clipboard"), or the empty state "No audit events". The actor of an account is
// a link to /<org>/<project>/members?highlight=<account id> for an Administrator. The key filter matches the key
// exactly on a deployed target (the mock ignores the case), and the store keeps keys in lower case.
public sealed partial class AuditTrail(App app, AppRoutes routes)
{
    // The columns of the table, in their order (Cell(row, column)).
    public const int TimeColumn = 0;
    public const int OperationColumn = 1;
    public const int ViewColumn = 2;
    public const int KeyColumn = 3;
    public const int VersionColumn = 4;
    public const int LevelColumn = 5;
    public const int ActorColumn = 6;
    public const int ChannelColumn = 7;
    public const int CountColumn = 8;
    public const int CorrelationColumn = 9;

    public const string EveryOperation = "Every operation";

    public const string NoEventsTitle = "No audit events";

    public const string NoEventsDescription = "Reveals, writes, exports, policy and key changes of secrets are recorded here for 400 days.";

    public ILocator ViewFilter => app.Page.GetByRole(AriaRole.Textbox, new() { Name = "View filter", Exact = true });

    public ILocator KeyFilter => app.Page.GetByRole(AriaRole.Textbox, new() { Name = "Key filter", Exact = true });

    public ILocator OperationFilter => app.Page.GetByRole(AriaRole.Combobox, new() { Name = "Operation filter", Exact = true });

    // The date inputs of the tab (only the open tab is mounted, so the Usage tab's From and To are not there).
    public ILocator From => app.Page.GetByLabel("From", new() { Exact = true });

    public ILocator To => app.Page.GetByLabel("To", new() { Exact = true });

    // "<n> event(s)": the events listed (0 while they load).
    public ILocator Counter => app.Page.GetByText(CounterText());

    // The table of the events, the one with the column "Correlation".
    public ILocator Table => app.Page.GetByRole(AriaRole.Table).Filter(new() { Has = app.Page.GetByRole(AriaRole.Columnheader, new() { Name = "Correlation", Exact = true }) });

    // The events, newest first.
    public ILocator Rows => Table.Locator("tbody tr");

    public ILocator NoEvents => app.Page.GetByRole(AriaRole.Heading, new() { Name = NoEventsTitle, Exact = true, Level = 2 });

    public ILocator NoEventsText => app.Page.GetByText(NoEventsDescription, new() { Exact = true });

    // The counter's text for a number of events.
    public static string CounterOf(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return $"{count} event(s)";
    }

    // A cell of an event's row by its column (TimeColumn … CorrelationColumn).
    public static ILocator Cell(ILocator row, int column)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(column, CorrelationColumn);

        return row.GetByRole(AriaRole.Cell).Nth(column);
    }

    // The link of an account actor (to the members page, for an Administrator).
    public static ILocator ActorLink(ILocator row)
    {
        return Cell(row, ActorColumn).GetByRole(AriaRole.Link);
    }

    public static ILocator CopyCorrelationButton(ILocator row)
    {
        return Cell(row, CorrelationColumn).GetByRole(AriaRole.Button, new() { Name = "Copy to clipboard", Exact = true });
    }

    // An option of the open Operation filter, by its exact label.
    public ILocator OperationOption(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        return app.Page.GetByRole(AriaRole.Option, new() { Name = operation, Exact = true });
    }

    // Opens Monitor's Audit tab, a page load (the filters start empty), and waits for its toolbar.
    public async Task OpenAsync()
    {
        await app.GotoAsync(routes.Monitor("audit"));
        await app.WaitForReadyAsync();
        await Expect(KeyFilter).ToBeVisibleAsync();
    }

    // Picks the operation ("Every operation" for all) in Operation filter and waits until the select shows it.
    public async Task PickOperationAsync(string operation)
    {
        await OperationFilter.ClickAsync();
        await OperationOption(operation).ClickAsync();
        await Expect(OperationFilter).ToContainTextAsync(operation);
    }

    // The start of the correlation id that the row shows before its "…".
    public static async Task<string> CorrelationPrefixAsync(ILocator row)
    {
        var text = await Cell(row, CorrelationColumn).InnerTextAsync();
        var match = CorrelationText().Match(text);
        return match.Success
            ? match.Groups["prefix"].Value
            : throw new InvalidOperationException($"The correlation cell shows '{text}', not the start of an id and '…'.");
    }

    [GeneratedRegex(@"^\d+ event\(s\)$")]
    private static partial Regex CounterText();

    [GeneratedRegex(@"^\s*(?<prefix>\S+)…")]
    private static partial Regex CorrelationText();
}
