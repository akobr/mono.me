using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Effective tab of a configuration (components/configuration/ConfigurationEffective.vue and
// ConfigurationComposition.vue), inside the detail's region. The radiogroup "Effective view" has "Document" (the
// default) and "Composition" (?show=composition); the editor of either view has a format toggle of its own, so the
// Edit tab's "Editing mode" is not this one.
// - Document: the merge order ("Ancestors, then the type template, then the own content, merged in this order.", the
//   start of a longer paragraph) and the read-only editor "Effective configuration". An annotation that does not
//   exist shows the empty state "No configuration" instead.
// - Composition: the heading "Steps, in the order applied" over a li[data-step="<order>"] per merged document, with
//   the toggle "Show the lines of step <order>" (aria-pressed) and its source: a link for an ancestor's configuration in
//   the catalog (its Edit tab, with the list's q), bold text for the own content, "Template · <Type>" for a template.
//   The read-only editor "Effective configuration, by step" marks each line in its gutter with the order of the step
//   that set it ("<order>", or "<order>T" for a template step), and a status line (aria-live) names the selected line,
//   "Line <n>: step <order> · <source>".
// A deployed API composes deeper than the mock (an ancestor with its own ancestors and template, steps applied again),
// so a test reads a step's order from its li, never hard-codes it.
public sealed partial class ConfigurationEffective(App app, ILocator scope)
{
    public const string MergeOrderText = "Ancestors, then the type template, then the own content, merged in this order.";

    // The gutter marker level with the editor line that contains the text: the composition's markers are the
    // gutter's elements, one per line, at the line's height (the hidden spacer keeps the gutter's width). The same
    // reading as the TS e2e's markerOf (test/e2e/configuration.spec.ts).
    private const string ReadLineMarker = """
        ([content, needle]) => {
          const editor = content.closest('.cm-editor') ?? document;
          const line = [...editor.querySelectorAll('.cm-line')].find(element => element.textContent?.includes(needle));
          const box = line?.getBoundingClientRect();
          const marker = [...editor.querySelectorAll('.cm-composition-gutter .cm-composition-marker')].find(element => {
            const rect = element.getBoundingClientRect();
            const middle = (rect.top + rect.bottom) / 2;
            return getComputedStyle(element).visibility !== 'hidden' && !!box && middle > box.top && middle < box.bottom;
          });
          return marker?.textContent?.trim() ?? null;
        }
        """;

    public ILocator ViewGroup => scope.GetByRole(AriaRole.Radiogroup, new() { Name = "Effective view", Exact = true });

    public ILocator DocumentView => ViewGroup.GetByRole(AriaRole.Radio, new() { Name = "Document", Exact = true });

    public ILocator CompositionView => ViewGroup.GetByRole(AriaRole.Radio, new() { Name = "Composition", Exact = true });

    // The paragraph that explains the merge order (it goes on about the bindings).
    public ILocator MergeOrder => scope.GetByText(MergeOrderText);

    public CodeEditor Document => new(app, "Effective configuration", scope);

    // The empty state of an annotation that does not exist (or is deleted), and its description.
    public ILocator NoConfigurationHeading => scope.GetByRole(AriaRole.Heading, new() { Name = "No configuration", Exact = true });

    public ILocator NothingInherited => scope.GetByText("Nothing is configured or inherited for this annotation yet.", new() { Exact = true });

    public ILocator StepsHeading => scope.GetByRole(AriaRole.Heading, new() { Name = "Steps, in the order applied", Exact = true });

    public ILocator Steps => scope.Locator("li[data-step]");

    public CodeEditor ByStep => new(app, "Effective configuration, by step", scope);

    // The gutter of the composition's editor; only the Composition view has it.
    public ILocator CompositionGutter => scope.Locator(".cm-composition-gutter");

    // The status line under the composition: "Line <n>: step <order> · <source>", "Line <n>: the document itself" or
    // "Step <order> · <source>: sets no value".
    public ILocator CompositionStatus => scope.GetByText(StatusText());

    // The step whose source is exactly the key (an ancestor's configuration or the own content).
    public ILocator Step(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Steps.Filter(new() { Has = app.Page.GetByText(key, new() { Exact = true }) });
    }

    // The link of an ancestor's step to its configuration; the own step has none.
    public ILocator StepLink(string key)
    {
        return Step(key).GetByRole(AriaRole.Link, new() { Name = key, Exact = true });
    }

    // The toggle of a step, "Show the lines of step <order>" (aria-pressed while its lines are highlighted).
    public ILocator StepToggle(string order)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(order);
        return scope.GetByRole(AriaRole.Button, new() { Name = $"Show the lines of step {order}", Exact = true });
    }

    // The order of the step whose source is the key, its data-step.
    public async Task<string> StepOrderAsync(string key)
    {
        var step = Step(key);
        await Expect(step).ToBeVisibleAsync();
        return await step.GetAttributeAsync("data-step")
            ?? throw new InvalidOperationException($"The composition's step of {key} has no data-step.");
    }

    // Shows the Composition view and waits for its steps and its editor.
    public async Task ShowCompositionAsync()
    {
        await CompositionView.ClickAsync();
        await app.ExpectQueryAsync("show", "composition");
        await Expect(StepsHeading).ToBeVisibleAsync();
        await Expect(ByStep.Content).ToBeVisibleAsync();
    }

    // Shows the Document view (?show= is gone) and waits for its editor.
    public async Task ShowDocumentAsync()
    {
        await DocumentView.ClickAsync();
        await app.ExpectQueryAsync("show", null);
        await Expect(Document.Content).ToBeVisibleAsync();
    }

    // Waits until the gutter marks the line that contains the text with the expected marker ("<order>", "<order>T" for
    // a template step), up to Execution:ActionTimeoutMs; on a timeout it names the marker the line has. Keep the
    // document short: CodeMirror renders only the lines near the viewport.
    public async Task ExpectLineMarkerAsync(string lineText, string expected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineText);
        ArgumentException.ThrowIfNullOrWhiteSpace(expected);

        var content = await ByStep.Content.ElementHandleAsync();
        try
        {
            await app.Page.WaitForFunctionAsync(
                $"([content, needle, expected]) => ({ReadLineMarker})([content, needle]) === expected",
                new object[] { content, lineText, expected },
                new() { PollingInterval = 100 });
        }
        catch (TimeoutException exception)
        {
            var actual = await app.Page.EvaluateAsync<string?>(ReadLineMarker, new object[] { content, lineText });
            throw new TimeoutException($"The line with '{lineText}' of the composition is not marked '{expected}'; its marker is '{actual ?? "none"}'.", exception);
        }
    }

    // GetByText's regex sees the text with the spaces of the template around it.
    [GeneratedRegex(@"^\s*(Line \d+: |Step \d+ · )")]
    private static partial Regex StatusText();
}
