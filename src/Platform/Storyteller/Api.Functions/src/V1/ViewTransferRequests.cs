using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace _42.Platform.Storyteller.Api.V1;

// The options of the export and import requests of a view (export-import.md, API). Kinds, formats and the existing mode
// are their names in any case; numbers are not accepted.
internal static class ViewTransferRequests
{
    private static readonly JsonDocumentOptions BodyOptions = new() { AllowTrailingCommas = true };

    // The include query parameter of an import: comma-separated, or repeated (HttpRequestData.Query joins the values with
    // commas). Null when it is missing or empty, which means all four kinds. An unknown kind is 400 InvalidQuery.
    public static IReadOnlyList<ViewArchiveContent>? ParseInclude(string? raw)
    {
        var names = raw?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

        if (names.Length == 0)
        {
            return null;
        }

        return names
            .Select(name => TryParseName<ViewArchiveContent>(name, out var content)
                ? content
                : throw new InvalidInputException(
                    $"The query parameter '{Definitions.Parameters.Include}' has the unknown kind '{name}'. Use {Names<ViewArchiveContent>()}, comma-separated, or leave it empty for all four.",
                    ErrorCodes.InvalidQuery))
            .ToList();
    }

    // The existing query parameter of an import: Overwrite when it is missing or empty. Anything else is 400 InvalidQuery.
    public static ViewImportExisting ParseExisting(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return ViewImportExisting.Overwrite;
        }

        return TryParseName<ViewImportExisting>(raw.Trim(), out var existing)
            ? existing
            : throw new InvalidInputException(
                $"The query parameter '{Definitions.Parameters.Existing}' must be {Names<ViewImportExisting>()}, not '{raw}'.",
                ErrorCodes.InvalidQuery);
    }

    // The body of an export: { "Format": "Folder" | "Git", "Include": [ ... ] }, member names in any case. No body, an
    // empty one or null members export a folder of all four kinds. Other members are ignored, as by the model binder.
    public static bool TryParseExport(string body, [NotNullWhen(true)] out ViewExportCreate? export, [NotNullWhen(false)] out string? error)
    {
        export = new ViewExportCreate();
        error = null;

        if (string.IsNullOrWhiteSpace(body))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(body, BodyOptions);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                export = null;
                error = "The body must be a JSON object: { \"Format\": \"Folder\" | \"Git\", \"Include\": [ ... ] }.";
                return false;
            }

            foreach (var member in document.RootElement.EnumerateObject())
            {
                if (member.Name.Equals(nameof(ViewExportCreate.Format), StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParseFormat(member.Value, out var format, out error))
                    {
                        export = null;
                        return false;
                    }

                    export = export with { Format = format };
                }
                else if (member.Name.Equals(nameof(ViewExportCreate.Include), StringComparison.OrdinalIgnoreCase))
                {
                    if (!TryParseInclude(member.Value, out var include, out error))
                    {
                        export = null;
                        return false;
                    }

                    export = export with { Include = include };
                }
            }

            return true;
        }
        catch (JsonException exception)
        {
            export = null;
            error = $"The body is not valid JSON: {exception.Message}";
            return false;
        }
    }

    private static bool TryParseFormat(JsonElement value, out ViewArchiveFormat format, [NotNullWhen(false)] out string? error)
    {
        format = ViewArchiveFormat.Folder;
        error = null;

        if (value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.String && TryParseName(value.GetString()!, out format))
        {
            return true;
        }

        error = $"Unknown archive format {value.GetRawText()}. Use {Names<ViewArchiveFormat>()}.";
        return false;
    }

    private static bool TryParseInclude(JsonElement value, out IReadOnlyList<ViewArchiveContent>? include, [NotNullWhen(false)] out string? error)
    {
        include = null;
        error = null;

        if (value.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            error = $"{nameof(ViewExportCreate.Include)} must be a list of kinds: {Names<ViewArchiveContent>()}.";
            return false;
        }

        var kinds = new List<ViewArchiveContent>();

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || !TryParseName<ViewArchiveContent>(item.GetString()!, out var content))
            {
                error = $"{nameof(ViewExportCreate.Include)} has the unknown kind {item.GetRawText()}. Use {Names<ViewArchiveContent>()}, or an empty list for all four.";
                return false;
            }

            kinds.Add(content);
        }

        // an empty list is all four kinds, as a missing one
        include = kinds.Count == 0 ? null : kinds;
        return true;
    }

    // A defined name in any case; Enum.TryParse would also take numbers and comma-joined names.
    private static bool TryParseName<TEnum>(string name, out TEnum value)
        where TEnum : struct, Enum
    {
        var match = Enum.GetNames<TEnum>().FirstOrDefault(candidate => candidate.Equals(name, StringComparison.OrdinalIgnoreCase));
        value = match is null ? default : Enum.Parse<TEnum>(match);
        return match is not null;
    }

    private static string Names<TEnum>()
        where TEnum : struct, Enum
    {
        var names = Enum.GetNames<TEnum>();
        return $"{string.Join(", ", names.Take(names.Length - 1))} or {names[^1]}";
    }
}
