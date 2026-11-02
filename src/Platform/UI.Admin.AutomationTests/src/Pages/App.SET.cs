using System.Text.RegularExpressions;

namespace _42.Platform.UI.Admin.AutomationTests.Pages;

// The Settings section's members of the shell: what the browser keeps (localStorage) and the type colors of the
// document. The settings of this browser apply without a Save and without a toast, so the tests read their effect here.
public sealed partial class App
{
    // Waits until the JSON that localStorage keeps under the key has the text value at the path ("acme.billing",
    // "defaultTimeZone"), or none for null, up to Execution:ActionTimeoutMs; the error shows what is stored.
    public async Task ExpectStoredSettingAsync(string key, IReadOnlyList<string> path, string? expected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(path);

        try
        {
            await Page.WaitForFunctionAsync(
                """
                ([key, path, expected]) => {
                  let value;
                  try {
                    value = JSON.parse(localStorage.getItem(key) ?? 'null');
                  } catch {
                    return false;
                  }
                  for (const part of path) value = value?.[part];
                  return (value ?? null) === expected;
                }
                """,
                new object?[] { key, path.ToArray(), expected },
                new() { PollingInterval = 100 });
        }
        catch (TimeoutException exception)
        {
            var stored = await Page.EvaluateAsync<string?>("key => localStorage.getItem(key)", key);
            var what = expected is null ? "no value" : $"'{expected}'";
            throw new TimeoutException($"localStorage '{key}' did not get {what} at {string.Join('.', path)}; it holds {stored ?? "nothing"}.", exception);
        }
    }

    // Waits until the CSS variable --st-<code> of the document (the color of an annotation type, e.g. exe) is the
    // color ("#b91c1c"), up to Execution:ActionTimeoutMs.
    public async Task ExpectTypeColorAsync(string code, string color)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(color);

        if (!TypeCode().IsMatch(code))
        {
            throw new ArgumentException($"'{code}' is not a type code.", nameof(code));
        }

        const string read = "name => getComputedStyle(document.documentElement).getPropertyValue(name).trim()";
        var name = $"--st-{code}";
        try
        {
            await Page.WaitForFunctionAsync(
                $"([name, color]) => ({read})(name) === color",
                new object[] { name, color },
                new() { PollingInterval = 100 });
        }
        catch (TimeoutException exception)
        {
            var actual = await Page.EvaluateAsync<string>(read, name);
            throw new TimeoutException($"The CSS variable {name} did not become {color}; it is '{actual}'.", exception);
        }
    }

    [GeneratedRegex("^[a-z]{3}$")]
    private static partial Regex TypeCode();
}
