using System.Text.Json.Nodes;

namespace _42.Platform.UI.Admin.AutomationTests.Support;

// Checks of the files the values store's Export downloads (DownloadedFile): the key/value document
// ({"Format":"2s.key-values.v1","Items":[{"Key","Protection","Value"?,"ContentType"?}]}, a secret without "Value"
// unless the secret values were included), the flat JSON object (key → value, secrets without a value left out) and
// the .env lines (KEY__PART=value, a key's '.' as "__" in upper case). Each check throws with what the file has
// instead for the test's own key, never with the file's text, which may hold other keys of the view.
public static class KeyValueExportFiles
{
    public const string DocumentFormat = "2s.key-values.v1";

    public static void ExpectFileName(DownloadedFile file, string expected)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.SuggestedFilename != expected)
        {
            throw new InvalidOperationException($"The export downloaded {file.SuggestedFilename}, not {expected}.");
        }
    }

    // The items of a key/value document, after a check of its format.
    public static IReadOnlyList<JsonObject> DocumentItems(DownloadedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);

        var document = JsonNode.Parse(file.Text)?.AsObject()
            ?? throw new InvalidOperationException($"{file.SuggestedFilename} is no JSON object.");
        var format = document["Format"]?.GetValue<string>();
        if (format != DocumentFormat)
        {
            throw new InvalidOperationException($"{file.SuggestedFilename} has the format '{format}', not '{DocumentFormat}'.");
        }

        var items = document["Items"]?.AsArray() ?? throw new InvalidOperationException($"{file.SuggestedFilename} has no Items.");
        return items.OfType<JsonObject>().ToList();
    }

    // The item of the key has the level and the value (null: the item has no value).
    public static void ExpectItem(IReadOnlyList<JsonObject> items, string key, string protection, string? value)
    {
        ArgumentNullException.ThrowIfNull(items);

        var item = items.SingleOrDefault(candidate => string.Equals(candidate["Key"]?.GetValue<string>(), key, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"The key/value document has no item '{key}'.");
        var actualProtection = item["Protection"]?.GetValue<string>();
        var actualValue = item["Value"]?.GetValue<string>();
        if (actualProtection != protection || actualValue != value)
        {
            throw new InvalidOperationException(
                $"The item '{key}' of the key/value document is {actualProtection} with {Describe(actualValue)}, not {protection} with {Describe(value)}.");
        }
    }

    // No item of a secret level carries a value.
    public static void ExpectNoSecretValues(IReadOnlyList<JsonObject> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var withValues = items
            .Where(item => item["Protection"]?.GetValue<string>() is { } protection && protection != "Public" && item["Value"] is not null)
            .Select(item => item["Key"]?.GetValue<string>())
            .ToList();
        if (withValues.Count > 0)
        {
            throw new InvalidOperationException($"The key/value document carries the values of the secrets {string.Join(", ", withValues)}.");
        }
    }

    public static JsonObject FlatValues(DownloadedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return JsonNode.Parse(file.Text)?.AsObject() ?? throw new InvalidOperationException($"{file.SuggestedFilename} is no JSON object.");
    }

    // The flat JSON has the value for the key (null: the key is not in it).
    public static void ExpectFlatValue(JsonObject values, string key, string? value)
    {
        ArgumentNullException.ThrowIfNull(values);

        var actual = values.TryGetPropertyValue(key, out var node) ? node?.GetValue<string>() : null;
        if (actual != value)
        {
            throw new InvalidOperationException($"The flat JSON has {Describe(actual)} for '{key}', not {Describe(value)}.");
        }
    }

    // The .env file has the exact line.
    public static void ExpectEnvLine(DownloadedFile file, string line)
    {
        if (!EnvLines(file).Contains(line, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"{file.SuggestedFilename} has no line '{line}'.");
        }
    }

    // The .env file has no line of the key (its .env form).
    public static void ExpectNoEnvLine(DownloadedFile file, string envKey)
    {
        if (EnvLines(file).Any(line => line.StartsWith($"{envKey}=", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException($"{file.SuggestedFilename} has a line of {envKey}, which it should leave out.");
        }
    }

    // The file does not carry the text anywhere (a secret value).
    public static void ExpectAbsent(DownloadedFile file, string text)
    {
        ArgumentNullException.ThrowIfNull(file);

        if (file.Text.Contains(text, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{file.SuggestedFilename} carries a secret value, which it should leave out.");
        }
    }

    private static string[] EnvLines(DownloadedFile file)
    {
        ArgumentNullException.ThrowIfNull(file);
        return file.Text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
    }

    private static string Describe(string? value)
    {
        return value is null ? "no value" : $"the value '{value}'";
    }
}
