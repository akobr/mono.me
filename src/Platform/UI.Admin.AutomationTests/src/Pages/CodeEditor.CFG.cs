using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Configurations section's part of the editor: the binding assist of a JSON or YAML document
// (components/editor/setup/bindings.ts). Typing "@" in a string value opens CodeMirror's completion list
// (.cm-tooltip-autocomplete, options with the role option): a function such as "config…", then the paths of the
// configuration ("/count"); an option must be clicked, a key within 75 ms after the list opens is ignored. A binding
// string is marked (.cm-binding), and hovering it opens a card (.cm-tooltip-hover) with "Function config", its summary,
// the syntax and an example. CodeMirror gives the lists and the card no names, hence its classes.
public sealed partial class CodeEditor
{
    private const int HoverAttempts = 5;

    // The open completion list; teleported out of the editor, but there is one at a time.
    public ILocator BindingCompletions => app.Page.Locator(".cm-tooltip-autocomplete");

    // The card of the hovered binding.
    public ILocator BindingHoverCard => app.Page.Locator(".cm-tooltip-hover");

    // The binding strings of the document.
    public ILocator BindingMarks => Content.Locator(".cm-binding");

    // An option of the open completion list whose name starts with the text, e.g. "config" (its name goes on with the
    // signature).
    public ILocator BindingCompletionStartingWith(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return BindingCompletions.GetByRole(AriaRole.Option, new() { NameRegex = new Regex($"^{Regex.Escape(text)}") });
    }

    // An option of the open completion list by its exact name, e.g. a path "/count".
    public ILocator BindingCompletion(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return BindingCompletions.GetByRole(AriaRole.Option, new() { Name = name, Exact = true });
    }

    // Hovers the first binding of the document until its card contains the text. The card opens after the pointer
    // rests on the binding, and under load a single move can be missed: the pointer leaves and comes back, a few times,
    // each waiting up to 2 seconds (the TS e2e retries the same way).
    public async Task HoverBindingAsync(string expected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expected);

        for (var attempt = 1; ; attempt++)
        {
            await app.Page.Mouse.MoveAsync(0, 0);
            await BindingMarks.First.HoverAsync();
            try
            {
                await Expect(BindingHoverCard).ToContainTextAsync(expected, new() { Timeout = 2_000 });
                return;
            }
            catch (PlaywrightException) when (attempt < HoverAttempts)
            {
                // The card did not open for this move; move again.
            }
        }
    }
}
