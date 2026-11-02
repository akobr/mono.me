using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The names, colors and icons of the seven annotation types (components/annotations/TypeAppearanceEditor.vue) in a card:
// the project's "Annotation types" on Settings and the personal one of the account page. A row per type, in the order
// rst, unt, sbt, usg, cnt, exe, uxe: the preview (test id type-preview-<code>: the icon and the name in effect), the
// textboxes "<Type> name" (at most 40 characters) and "<Type> plural" (disabled, titled "Set a name first", until the
// row has a name of its own; its placeholder is the plural of that name), the combobox "<Type> color" ("Default
// <inherited color>", then Red … Gray), the button "<Type> icon" with its search "Search icons…" and, once the row has
// a value of its own, "Reset <Type>". Every name is the built-in type name, whatever the type is called now. A name
// applies on change, so it is filled and then left with Tab; nothing is saved to the API and no toast shows. The
// colors are the CSS variables --st-<code> of the document (App.ExpectTypeColorAsync).
public sealed partial class TypeAppearanceEditor(App app, ILocator card)
{
    // The longest name or plural the fields and the stores keep.
    public const int MaxNameLength = 40;

    // The built-in name of each type code, which names the fields of its row.
    private static readonly Dictionary<string, string> BuiltInNames = new(StringComparer.Ordinal)
    {
        ["rst"] = "Responsibility",
        ["unt"] = "Unit",
        ["sbt"] = "Subject",
        ["usg"] = "Usage",
        ["cnt"] = "Context",
        ["exe"] = "Execution",
        ["uxe"] = "Unit of execution",
    };

    // The type codes in the editor's order (rst, unt, sbt, usg, cnt, exe, uxe).
    public static IReadOnlyList<string> Codes { get; } = ["rst", "unt", "sbt", "usg", "cnt", "exe", "uxe"];

    // The search box of the open icon list.
    public ILocator IconSearch => app.Page.GetByPlaceholder("Search icons…", new() { Exact = true });

    // The built-in name of a type code ("exe" is "Execution").
    public static string BuiltInName(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return BuiltInNames.TryGetValue(code, out var name)
            ? name
            : throw new ArgumentException($"'{code}' is not a type code; use one of {string.Join(", ", Codes)}.", nameof(code));
    }

    // The preview of a type: its icon and the name in effect for this layer.
    public ILocator Preview(string code)
    {
        BuiltInName(code);
        return card.GetByTestId($"type-preview-{code}");
    }

    public ILocator Name(string code)
    {
        return card.GetByRole(AriaRole.Textbox, new() { Name = $"{BuiltInName(code)} name", Exact = true });
    }

    public ILocator Plural(string code)
    {
        return card.GetByRole(AriaRole.Textbox, new() { Name = $"{BuiltInName(code)} plural", Exact = true });
    }

    public ILocator Color(string code)
    {
        return card.GetByRole(AriaRole.Combobox, new() { Name = $"{BuiltInName(code)} color", Exact = true });
    }

    public ILocator Icon(string code)
    {
        return card.GetByRole(AriaRole.Button, new() { Name = $"{BuiltInName(code)} icon", Exact = true });
    }

    // Shown while the row has a value of its own (and the editor is not read-only).
    public ILocator Reset(string code)
    {
        return card.GetByRole(AriaRole.Button, new() { Name = $"Reset {BuiltInName(code)}", Exact = true });
    }

    // Fills the type's name and leaves the field with Tab, which applies it; returns once the preview shows it.
    public async Task SetNameAsync(string code, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        await Name(code).FillAsync(name);
        await Name(code).PressAsync("Tab");
        await Expect(Preview(code)).ToHaveTextAsync(name.Trim());
    }

    // Fills the type's plural and leaves the field with Tab, which applies it; returns once the field keeps it.
    public async Task SetPluralAsync(string code, string plural)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plural);

        await Plural(code).FillAsync(plural);
        await Plural(code).PressAsync("Tab");
        await Expect(Plural(code)).ToHaveValueAsync(plural.Trim());
    }

    // Picks a color by its label ("Red", "Gray", …) and waits until the select shows it.
    public async Task PickColorAsync(string code, string color)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(color);

        await Color(code).ClickAsync();
        await app.Page.GetByRole(AriaRole.Option, new() { Name = color, Exact = true }).ClickAsync();
        await Expect(Color(code)).ToHaveTextAsync(color);
    }

    // Searches an icon by its label ("Rocket", …), picks it and waits until the button shows it.
    public async Task PickIconAsync(string code, string icon)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(icon);

        await Icon(code).ClickAsync();
        await IconSearch.FillAsync(icon);
        await app.Page.GetByRole(AriaRole.Option, new() { Name = icon, Exact = true }).ClickAsync();
        await Expect(Icon(code)).ToHaveTextAsync(icon);
    }
}
