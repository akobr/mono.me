using System.Text.Json;
using Microsoft.Playwright;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// A CodeMirror 6 editor of the admin UI (components/editor/CodeEditor.vue, also inside DocumentEditor for the JSON and
// YAML documents): its content element is a textbox named by the editor, e.g. "Own configuration", "Effective
// configuration", "Schema document", "Template", "Annotation document" or "Version 3". A test replaces the text as one
// insertion, not by typing: typing gets CodeMirror's closed brackets, indentation and completions. CodeMirror renders
// only the lines in view (plus a margin), so TextAsync and ExpectTextAsync read the editor's document, not the page;
// Expect(Content).ToContainTextAsync works for a document that fits. Ctrl/Cmd+S inside the editor saves where the page
// offers a save (it is the editor's own key binding, so the page's window shortcut does not save a second time).
public sealed partial class CodeEditor
{
    // The document of the editor: CodeMirror keeps its view on the content element (cmTile from @codemirror/view 6.39,
    // cmView before); without one, the text of the rendered lines.
    private const string ReadText = """
        content => {
          const view = (content.cmTile ?? content.cmView)?.view;
          return view?.state?.doc ? view.state.doc.toString() : [...content.querySelectorAll('.cm-line')].map(line => line.textContent ?? '').join('\n');
        }
        """;

    private readonly App app;

    // name: the exact accessible name of the editor; scope: where to look for it (the page by default), e.g. a detail.
    public CodeEditor(App app, string name, ILocator? scope = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        this.app = app;
        Name = name;
        Content = scope is null
            ? app.Page.GetByRole(AriaRole.Textbox, new() { Name = name, Exact = true })
            : scope.GetByRole(AriaRole.Textbox, new() { Name = name, Exact = true });
    }

    public string Name { get; }

    // CodeMirror's content element (contenteditable, contenteditable="false" when the editor is read-only).
    public ILocator Content { get; }

    // The whole text of the editor's document.
    public async Task<string> TextAsync()
    {
        return await Content.EvaluateAsync<string>(ReadText);
    }

    // Waits until the editor's document is exactly the text, up to Execution:ActionTimeoutMs. CodeMirror keeps its lines
    // with \n, so the text's \r\n (JsonSerializer's indentation on Windows) count as \n.
    public async Task ExpectTextAsync(string expected)
    {
        ArgumentNullException.ThrowIfNull(expected);
        await ExpectTextAsync(new[] { expected });
    }

    // Waits until the editor's document is exactly one of the texts, up to Execution:ActionTimeoutMs, and returns the
    // one it holds (as given), for an outcome with two accepted states (the one a case intends and the one of a known
    // bug of the admin UI). Line endings count as \n, as in ExpectTextAsync(string).
    public async Task<string> ExpectTextAsync(params string[] alternatives)
    {
        ArgumentNullException.ThrowIfNull(alternatives);
        if (alternatives.Length == 0 || alternatives.Any(text => text is null))
        {
            throw new ArgumentException("Give at least one text, and no null.", nameof(alternatives));
        }

        var expected = alternatives.Select(text => text.ReplaceLineEndings("\n")).ToArray();
        var content = await Content.ElementHandleAsync();
        try
        {
            // The position of the text plus one, so that a match is truthy also for the first text.
            var found = await app.Page.WaitForFunctionAsync(
                $"([content, expected]) => expected.indexOf(({ReadText})(content)) + 1",
                new object[] { content, expected },
                new() { PollingInterval = 100 });
            return alternatives[await found.JsonValueAsync<int>() - 1];
        }
        catch (TimeoutException exception)
        {
            // As JSON strings, so a difference in the spaces and line breaks shows.
            var actual = JsonSerializer.Serialize(await TextAsync());
            var texts = string.Join(" or ", expected.Select(text => JsonSerializer.Serialize(text)));
            throw new TimeoutException($"The text of the editor '{Name}' did not become {texts}; it is {actual}.", exception);
        }
    }

    // Replaces the whole document with the text, as one insertion (select all, then insert), and waits until the
    // editor holds exactly it. The text goes in with \n line breaks: an emulated phone's input keeps a \r in the
    // lines of CodeMirror's document, a desktop's does not. The editor must be editable.
    public async Task ReplaceAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        text = text.ReplaceLineEndings("\n");
        await Content.ClickAsync();
        await app.Page.Keyboard.PressAsync("ControlOrMeta+A");
        if (text.Length == 0)
        {
            await app.Page.Keyboard.PressAsync("Delete");
        }
        else
        {
            await app.Page.Keyboard.InsertTextAsync(text);
        }

        await ExpectTextAsync(text);
    }

    // Types the text at the cursor key by key, as a user would: CodeMirror closes brackets and quotes and indents new
    // lines, so the result is not the text alone. Focus the editor (and place the cursor) first.
    public async Task TypeAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        await app.Page.Keyboard.TypeAsync(text);
    }

    // Moves the keyboard focus into the editor without moving its cursor.
    public async Task FocusAsync()
    {
        await Content.FocusAsync();
    }

    // Ctrl/Cmd+S in the editor: the page's save, where it has one (the editor saves only when the page allows it, e.g.
    // a changed and valid document). A key press needs no free pointer: the toast of an earlier step can cover a Save
    // button at the bottom right, and a pointer on a toast keeps it open.
    public async Task PressSaveAsync()
    {
        await Content.PressAsync("ControlOrMeta+S");
    }
}
