using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// Locators of Nuxt UI parts that the page objects share. A form field (UFormField), an alert (UAlert) and a card
// (UCard) have no role of their own: a field is its label's nearest data-slot="root" (with its data-slot="help" and
// data-slot="error"), an alert its title's nearest data-slot="root" (with its data-slot="description"), a card its
// heading's nearest data-slot="root". A modal's own title and description use the same slot names, so an alert is
// found by its title text.
internal static class NuxtLocators
{
    // The whole text of an element, with the spaces Vue's template leaves around it; '/' is escaped as well, because
    // Playwright puts the regex between slashes.
    public static Regex Exactly(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new Regex($@"^\s*{Regex.Escape(text).Replace("/", @"\/", StringComparison.Ordinal)}\s*$");
    }

    // The form field of the scope by its exact label (without the '*' of a required field, which is only CSS).
    public static ILocator Field(ILocator scope, string label)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        return scope.Locator("[data-slot='label']").Filter(new() { HasTextRegex = Exactly(label) })
            .Locator("xpath=ancestor::*[@data-slot='root'][1]");
    }

    // The help under a form field (shown while it has no error).
    public static ILocator FieldHelp(ILocator scope, string label)
    {
        return Field(scope, label).Locator("[data-slot='help']");
    }

    // The error under a form field.
    public static ILocator FieldError(ILocator scope, string label)
    {
        return Field(scope, label).Locator("[data-slot='error']");
    }

    // The alert of the scope by its exact title.
    public static ILocator Alert(ILocator scope, string title)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return scope.Locator("[data-slot='title']").Filter(new() { HasTextRegex = Exactly(title) })
            .Locator("xpath=ancestor::*[@data-slot='root'][1]");
    }

    // The description of an alert (by its title).
    public static ILocator AlertDescription(ILocator scope, string title)
    {
        return Alert(scope, title).Locator("[data-slot='description']");
    }

    // The card of the scope around its heading, by the heading's exact name and level (h2, or h3 inside a section).
    public static ILocator Card(ILocator scope, string heading, int level)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(heading);

        return scope
            .GetByRole(AriaRole.Heading, new() { Name = heading, Exact = true, Level = level })
            .Locator("xpath=ancestor::*[@data-slot='root'][1]");
    }

    // An alert without a title (an API refusal in a dialog), by its exact description.
    public static ILocator Message(ILocator scope, string message)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return scope.Locator("[data-slot='description']").Filter(new() { HasTextRegex = Exactly(message) });
    }
}
