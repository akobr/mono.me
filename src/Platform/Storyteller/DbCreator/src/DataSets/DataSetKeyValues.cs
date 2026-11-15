using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using _42.Platform.Storyteller.KeyValues;

namespace _42.Platform.Storyteller.DbCreator.DataSets;

// The key/value pairs of a set: key-values.json at the root of its folder, in the format of the key/value export and
// import (2s.key-values.v1). Read checks every item with the rules of the import and the refusals of a seed, so a set
// that cannot be seeded is refused before anything is written; only the policy of an existing project is left to the
// import (DataSetKeyValueSeeder).
public sealed record class DataSetKeyValues
{
    public const string FileName = "key-values.json";

    // A set is ours, so the import's 1,000 items per call do not apply (the seed cuts the pairs into chunks); the
    // limits keep a file that is not a set's from being read whole.
    public const int MaxPairs = 10_000;

    public const long MaxFileBytes = 32L * 1024 * 1024;

    // The problems a refusal lists; the rest are counted.
    public const int MaxListedProblems = 20;

    // The levels a seed can write: Hsm, Byok and Hyok need keys of the organization, a customer's tenant or the
    // project's recipients, which a seed cannot set up.
    public static readonly IReadOnlyList<KeyValueProtection> SeedableLevels = [KeyValueProtection.Public, KeyValueProtection.Standard];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public DataSetKeyValues(IReadOnlyList<KeyValueDocumentItem> items)
    {
        Items = items;
        Levels = items
            .GroupBy(item => item.Protection)
            .OrderBy(group => group.Key)
            .Select(group => (group.Key, group.Count()))
            .ToList();
    }

    // In the order of the file.
    public IReadOnlyList<KeyValueDocumentItem> Items { get; }

    // The number of pairs per level in the order of the levels, levels without pairs left out.
    public IReadOnlyList<(KeyValueProtection Level, int Count)> Levels { get; }

    public int Count(KeyValueProtection level)
    {
        return Levels.Where(entry => entry.Level == level).Sum(entry => entry.Count);
    }

    // The pairs of the set in the folder, null without key-values.json. DataSetException when the file is beyond the
    // limits, does not parse, or holds an item a seed refuses; the message lists every problem.
    public static DataSetKeyValues? Read(IFileSystem fileSystem, string folder)
    {
        var path = fileSystem.Path.Combine(folder, FileName);

        if (!fileSystem.File.Exists(path))
        {
            return null;
        }

        if (fileSystem.FileInfo.New(path).Length > MaxFileBytes)
        {
            throw new DataSetException(
                $"The key/value pairs '{path}' cannot be read: the file is larger than {MaxFileBytes.ToString("N0", CultureInfo.InvariantCulture)} bytes.");
        }

        KeyValueDocument? document;

        try
        {
            document = JsonSerializer.Deserialize<KeyValueDocument>(fileSystem.File.ReadAllText(path), Options);
        }
        catch (JsonException exception)
        {
            throw new DataSetException($"The key/value pairs '{path}' are not valid: {exception.Message}", exception);
        }

        if (document is null)
        {
            throw Invalid(path, "it is not a JSON object");
        }

        // a missing Format counts as the current one, as sform story kv import takes it
        if (!string.Equals(document.Format, KeyValueDocument.CurrentFormat, StringComparison.Ordinal))
        {
            throw Invalid(path, $"the format '{document.Format}' is not supported; use '{KeyValueDocument.CurrentFormat}'");
        }

        if (document.Items is null)
        {
            throw Invalid(path, "Items is not an array of pairs");
        }

        if (document.Items.Count > MaxPairs)
        {
            throw Invalid(path, $"it has {document.Items.Count.ToString("N0", CultureInfo.InvariantCulture)} pairs, and a set holds at most {MaxPairs.ToString("N0", CultureInfo.InvariantCulture)}");
        }

        var problems = Validate(document.Items);

        if (problems.Count > 0)
        {
            var listed = problems.Take(MaxListedProblems).Select(problem => $"{Environment.NewLine}  {problem}");
            var more = problems.Count > MaxListedProblems
                ? $"{Environment.NewLine}  … and {(problems.Count - MaxListedProblems).ToString("N0", CultureInfo.InvariantCulture)} more"
                : string.Empty;
            throw new DataSetException(
                $"The key/value pairs '{path}' cannot be seeded: {Count(problems.Count, "item")} {(problems.Count == 1 ? "is" : "are")} not valid; nothing was written.{string.Concat(listed)}{more}");
        }

        return new DataSetKeyValues(document.Items);
    }

    // The problems of the items in the order of the file, one per item: "item 3 'smtp-host': …".
    public static IReadOnlyList<string> Validate(IReadOnlyList<KeyValueDocumentItem?> items)
    {
        var problems = new List<string>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var index = 0; index < items.Count; index++)
        {
            var item = items[index];
            var number = index + 1;

            if (item is null)
            {
                problems.Add($"item {number}: it is not a pair");
                continue;
            }

            var problem = GetProblem(item);

            if (problem is null && !seen.TryAdd(KeyValueRules.Normalize(item.Key), number))
            {
                problem = $"the key is listed more than once, ignoring case (first as item {seen[KeyValueRules.Normalize(item.Key)]})";
            }

            if (problem is not null)
            {
                problems.Add($"item {number} '{item.Key}': {problem}");
            }
        }

        return problems;
    }

    // The first rule the item breaks: the rules of the import (KeyValueRules, the shape of a reference), then the
    // refusals of a seed.
    private static string? GetProblem(KeyValueDocumentItem item)
    {
        try
        {
            KeyValueRules.EnsureKey(item.Key);

            if (!SeedableLevels.Contains(item.Protection))
            {
                return $"the level {item.Protection} cannot be seeded: Hsm, Byok and Hyok need keys of the organization, a customer's tenant or the project's recipients, which a seed cannot set up; use Public or Standard";
            }

            if (item.Value is null)
            {
                return "it has no Value: a seed creates the pair and has no existing secret to keep (an export without secret values cannot be seeded)";
            }

            KeyValueRules.EnsureValue(item.Value, item.Protection);
            KeyValueRules.EnsureContentType(item.ContentType);

            if (KeyValueContentTypes.IsReference(item.ContentType))
            {
                if (item.Protection != KeyValueProtection.Public)
                {
                    return "a secret reference is a Public pair; the platform never resolves it";
                }

                SecretReferences.Ensure(item.Value, item.ContentType!);
            }

            return null;
        }
        catch (InvalidInputException exception)
        {
            return exception.Message;
        }
    }

    private static DataSetException Invalid(string path, string reason)
    {
        return new DataSetException($"The key/value pairs '{path}' are not valid: {reason}.");
    }

    private static string Count(int count, string noun)
    {
        return $"{count.ToString("N0", CultureInfo.InvariantCulture)} {noun}{(count == 1 ? string.Empty : "s")}";
    }
}
