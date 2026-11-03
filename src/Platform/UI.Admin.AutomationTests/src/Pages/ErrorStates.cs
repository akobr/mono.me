using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The admin UI's error state (components/common/ErrorState.vue), which a page or a detail shows when the API refused a
// read or did not answer in time: an empty state (its title as an h2, the API's message, a hint and "Retry") or, compact,
// a Nuxt UI alert (the title, the message and the hint as its description, and the action "Retry"). A wait for an answer
// of the API ends on its Retry too, and reports its text.
internal static class ErrorStates
{
    private const string RetryLabel = "Retry";

    // The Retry buttons of the error states in the scope (a page or a detail panel).
    public static ILocator RetryButton(ILocator scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return scope.GetByRole(AriaRole.Button, new() { Name = RetryLabel, Exact = true });
    }

    // The error state around its Retry button: the nearest ancestor with the title (the empty state's h2, the alert's
    // title slot), whichever way the alert lays out its actions.
    public static ILocator Around(ILocator retry)
    {
        ArgumentNullException.ThrowIfNull(retry);
        return retry.Locator("xpath=ancestor::*[.//h2 or .//*[@data-slot='title']][1]");
    }

    // The text of an error state (its title, the API's message and the hint), of a failure's toast or of an alert, on
    // one line and without the label of Retry, for the message of a wait that ended on it. A rendered element gives its
    // lines (innerText); a hidden one (the list below a full-screen detail) has no layout, and its innerText would glue
    // the title, the message and "Retry" together, so it gives its text nodes, one per line.
    public static async Task<string> TextAsync(ILocator element)
    {
        ArgumentNullException.ThrowIfNull(element);

        var text = await element.EvaluateAsync<string>(
            """
            element => {
              if (element.getClientRects().length > 0) {
                return element.innerText;
              }
              const lines = [];
              const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
              for (let node = walker.nextNode(); node; node = walker.nextNode()) {
                lines.push(node.textContent);
              }
              return lines.join('\n');
            }
            """);
        return string.Join(" ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(line => line != RetryLabel));
    }
}
