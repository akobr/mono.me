using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The legend of the graph (components/graph/GraphLegend.vue): the group "Annotation types" with a chip per type in the
// fixed order rst, unt, sbt, usg, cnt, exe, uxe, then "All types" while a type is hidden. A chip is a toggle button:
// aria-pressed while its type is shown, titled "Hide <plural>" or "Show <plural>" (the type names of a fresh browser
// are the built-in ones), its text the plural and the count of the drawn set before the toggles (hiding does not lower
// it). Below 640 px of graph width (a phone) the legend is the button "Types" ("Types (<shown>/7)" while types are
// hidden), whose popover holds the same group; an empty view has neither.
public sealed partial class GraphLegend(App app)
{
    // The legend collapses into "Types" below this width of the graph.
    public const int CollapsedWidth = 640;

    // The type codes in the order of their chips.
    private static readonly string[] ChipOrder = ["rst", "unt", "sbt", "usg", "cnt", "exe", "uxe"];

    // The legend's group, in the graph or in the open popover of "Types".
    public ILocator Group => app.Page.GetByRole(AriaRole.Group, new() { Name = "Annotation types", Exact = true });

    // On a phone: "Types", or "Types (<shown>/7)" while types are hidden.
    public ILocator TypesButton => app.Page.GetByRole(AriaRole.Button, new() { NameRegex = TypesName() });

    public ILocator AllTypesButton => Group.GetByRole(AriaRole.Button, new() { Name = "All types", Exact = true });

    // The chips of the shown types.
    public ILocator PressedChips => Group.GetByRole(AriaRole.Button, new() { Pressed = true });

    // On a phone the graph is narrower than CollapsedWidth, so the legend is the "Types" popover.
    public bool IsCollapsed => app.Page.ViewportSize is { Width: < CollapsedWidth };

    // The chip of a type by its code (rst, unt, sbt, usg, cnt, exe, uxe), by its position: its name joins the plural
    // and the count, and "Units" is a prefix of "Units of execution".
    public ILocator Chip(string code)
    {
        var index = Array.IndexOf(ChipOrder, code);
        if (index < 0)
        {
            throw new ArgumentException($"'{code}' is not one of the type codes {string.Join(", ", ChipOrder)}.", nameof(code));
        }

        return Group.GetByRole(AriaRole.Button).Nth(index);
    }

    // The count of a chip, its last text.
    public ILocator Count(string code)
    {
        return Chip(code).Locator("span").Last;
    }

    // The chip by its title: "Hide subjects", "Show contexts", ….
    public ILocator ChipTitled(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        return Group.GetByTitle(title, new() { Exact = true });
    }

    // Shows the legend: on a phone opens the "Types" popover; nothing on a wider graph.
    public async Task OpenAsync()
    {
        if (IsCollapsed && !await Group.IsVisibleAsync())
        {
            await TypesButton.ClickAsync();
        }

        await Expect(Group).ToBeVisibleAsync();
    }

    // Closes the "Types" popover on a phone: with Escape (focus in the popover), or with a click on "Types" again where
    // an Escape would close something else (a deletion toast, whose Undo it loses); nothing on a wider graph.
    public async Task CloseAsync(bool withEscape = true)
    {
        if (!IsCollapsed || !await Group.IsVisibleAsync())
        {
            return;
        }

        if (withEscape)
        {
            await app.Page.Keyboard.PressAsync("Escape");
        }
        else
        {
            await TypesButton.ClickAsync();
        }

        await Expect(Group).ToBeHiddenAsync();
    }

    // Hides or shows a type with its chip (in the popover on a phone, which is closed again) and waits for the chip's
    // pressed state to change.
    public async Task ToggleAsync(string code, bool closeWithEscape = true)
    {
        await OpenAsync();
        var chip = Chip(code);
        var pressed = await chip.GetAttributeAsync("aria-pressed");
        await chip.ClickAsync();
        await Expect(chip).ToHaveAttributeAsync("aria-pressed", pressed == "true" ? "false" : "true");
        await CloseAsync(closeWithEscape);
    }

    // Shows every type with "All types" (in the popover on a phone, closed again).
    public async Task ShowAllTypesAsync()
    {
        await OpenAsync();
        await AllTypesButton.ClickAsync();
        await Expect(PressedChips).ToHaveCountAsync(ChipOrder.Length);
        await CloseAsync();
    }

    // Asserts the chip's state: pressed and "Hide <plural>" while shown, else "Show <plural>" (the built-in name).
    public async Task ExpectShownAsync(string code, string plural, bool shown)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plural);

        var chip = Chip(code);
        await Expect(chip).ToHaveAttributeAsync("aria-pressed", shown ? "true" : "false");
        await Expect(chip).ToHaveAttributeAsync("title", $"{(shown ? "Hide" : "Show")} {plural.ToLowerInvariant()}");
    }

    // Asserts the counts of all seven chips, in the chip order rst, unt, sbt, usg, cnt, exe, uxe.
    public async Task ExpectCountsAsync(params int[] counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        if (counts.Length != ChipOrder.Length)
        {
            throw new ArgumentException($"The legend has {ChipOrder.Length} chips; {counts.Length} counts were given.", nameof(counts));
        }

        for (var index = 0; index < ChipOrder.Length; index++)
        {
            await Expect(Count(ChipOrder[index])).ToHaveTextAsync(counts[index].ToString("N0", CultureInfo.GetCultureInfo("en-US")));
        }
    }

    // Asserts the "Types" button of a phone: "Types" with every type shown, "Types (<shown>/7)" otherwise.
    public async Task ExpectTypesButtonAsync(int shown)
    {
        var label = shown == ChipOrder.Length ? "Types" : string.Create(CultureInfo.InvariantCulture, $"Types ({shown}/{ChipOrder.Length})");
        await Expect(TypesButton).ToHaveAccessibleNameAsync(label);
    }

    // Hides every type: clicks the first pressed chip until none is left.
    public async Task HideAllAsync()
    {
        await OpenAsync();
        await Expect(PressedChips.First).ToBeVisibleAsync();
        while (await PressedChips.CountAsync() > 0)
        {
            var before = await PressedChips.CountAsync();
            await PressedChips.First.ClickAsync();
            await Expect(PressedChips).ToHaveCountAsync(before - 1);
        }
    }

    [GeneratedRegex(@"^Types")]
    private static partial Regex TypesName();
}
