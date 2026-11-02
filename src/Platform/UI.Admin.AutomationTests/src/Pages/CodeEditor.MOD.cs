using System.Text.Json;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Modeling section's member of CodeEditor: a wait for one of several texts, for an outcome with two accepted states
// (the one a case intends and the one of a known bug of the admin UI).
public sealed partial class CodeEditor
{
    // Waits until the editor's document is exactly one of the texts, up to Execution:ActionTimeoutMs, and returns the
    // one it holds (as given). Line endings count as \n, as in ExpectTextAsync(string).
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
}
