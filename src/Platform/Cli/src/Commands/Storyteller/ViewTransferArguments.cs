using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using _42.Platform.Storyteller.Sdk;

namespace _42.Platform.Cli.Commands.Storyteller;

// The options of `sform story export` and `sform story import`, checked before anything is sent: names in any case, as the
// API takes them (export-import.md, Request options); numbers are not accepted.
public static class ViewTransferArguments
{
    public const string FolderFormat = "folder";
    public const string GitFormat = "git";

    public static readonly IReadOnlyList<ViewArchiveContent> AllKinds = Enum.GetValues<ViewArchiveContent>();

    public static bool TryParseFormat(string? text, out ViewExportCreateFormat format, [NotNullWhen(false)] out string? error)
    {
        var name = text?.Trim();

        if (FolderFormat.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            format = ViewExportCreateFormat.Folder;
            error = null;
            return true;
        }

        if (GitFormat.Equals(name, StringComparison.OrdinalIgnoreCase))
        {
            format = ViewExportCreateFormat.Git;
            error = null;
            return true;
        }

        format = default;
        error = $"Unknown format '{text}'; use '{FolderFormat}' or '{GitFormat}'.";
        return false;
    }

    // The values of --include, comma-separated or repeated. Null when the option is missing: the server then takes all four
    // kinds. A kind named twice counts once, and the kinds are sent in import order.
    public static bool TryParseInclude(IReadOnlyCollection<string>? values, out IReadOnlyList<ViewArchiveContent>? include, [NotNullWhen(false)] out string? error)
    {
        include = null;
        error = null;

        if (values is null || values.Count == 0)
        {
            return true;
        }

        var kinds = new SortedSet<ViewArchiveContent>();

        foreach (var name in values.SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            if (!TryParseName<ViewArchiveContent>(name, out var kind))
            {
                error = $"Unknown kind '{name}' in --include; use {FormatKinds(AllKinds, "or")}, comma-separated.";
                return false;
            }

            kinds.Add(kind);
        }

        if (kinds.Count == 0)
        {
            error = $"The option --include names no kind; use {FormatKinds(AllKinds, "or")}, comma-separated, or leave it out for all four.";
            return false;
        }

        include = kinds.ToList();
        return true;
    }

    // The value of --existing; null when the option is missing, which the server takes as overwrite.
    public static bool TryParseExisting(string? text, out ViewImportExisting? existing, [NotNullWhen(false)] out string? error)
    {
        existing = null;
        error = null;

        if (text is null)
        {
            return true;
        }

        if (TryParseName<ViewImportExisting>(text.Trim(), out var parsed))
        {
            existing = parsed;
            return true;
        }

        error = $"Unknown value '{text}' of --existing; use 'overwrite' or 'skip'.";
        return false;
    }

    // "annotations, schemas, templates and configurations"; all four when the include is null or empty.
    public static string FormatKinds(IEnumerable<ViewArchiveContent>? kinds, string conjunction = "and")
    {
        var names = (kinds is null || !kinds.Any() ? AllKinds : kinds)
            .Select(kind => kind.ToString().ToLowerInvariant())
            .ToList();

        return names.Count == 1
            ? names[0]
            : $"{string.Join(", ", names.Take(names.Count - 1))} {conjunction} {names[^1]}";
    }

    // A defined name in any case; Enum.TryParse would also take numbers and comma-joined names.
    private static bool TryParseName<TEnum>(string name, out TEnum value)
        where TEnum : struct, Enum
    {
        var match = Enum.GetNames<TEnum>().FirstOrDefault(candidate => candidate.Equals(name, StringComparison.OrdinalIgnoreCase));
        value = match is null ? default : Enum.Parse<TEnum>(match);
        return match is not null;
    }
}
