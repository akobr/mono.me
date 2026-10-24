using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Mcp.Understanding;

// Searches configurations by their key and by the stored own documents. A stored document holds what was written on the
// annotation, not what it inherits: a value inherited from an ancestor is found on the ancestor. Binding expressions are
// matched and reported as stored; nothing is evaluated.
public sealed class ConfigurationSearch
{
    public const int MaxPages = 5;
    public const int MaxDocumentReads = 200;
    public const int DefaultMaxResults = 50;
    public const int MaxResultsLimit = 200;
    public const int MaxConcurrentReads = 4;

    public const string ByPages = "Pages";
    public const string ByDocumentReads = "DocumentReads";
    public const string ByMaxResults = "MaxResults";

    public const string PossibleReadNote =
        "In the $definition of an object binding: a possible read, made only if the program emits this string, because the resolver binds the program's result again.";

    // A larger object or array is reported by its kind only, so one match cannot fill the result.
    private const int MaxInlineValueLength = 1000;

    private readonly string? _subject;
    private readonly string? _responsibility;
    private readonly string? _context;
    private readonly string? _unit;
    private readonly string? _keyText;
    private readonly string? _property;
    private readonly string? _valueText;
    private readonly string? _bindsKey;

    public ConfigurationSearch(
        string? subject = null,
        string? responsibility = null,
        string? context = null,
        string? unit = null,
        string? keyText = null,
        string? property = null,
        string? valueText = null,
        string? bindsKey = null)
    {
        _subject = Normalize(subject);
        _responsibility = Normalize(responsibility);
        _context = Normalize(context);
        _unit = Normalize(unit);
        _keyText = Normalize(keyText);
        _property = Normalize(property);
        _valueText = Normalize(valueText);
        _bindsKey = Normalize(bindsKey)?.TrimStart('@');
    }

    public bool HasContentFilters => _property is not null || _valueText is not null || _bindsKey is not null;

    private bool HasKeyFilters => _subject is not null || _responsibility is not null || _context is not null || _unit is not null || _keyText is not null;

    public static int ClampMaxResults(int? maxResults)
    {
        return maxResults is null ? DefaultMaxResults : Math.Clamp(maxResults.Value, 1, MaxResultsLimit);
    }

    // The name filters match a case-insensitive part of the matching name in the key; keyText any name of it.
    public bool MatchesKey(string? annotationKey)
    {
        if (!HasKeyFilters)
        {
            return true;
        }

        if (!AnnotationKey.TryParse(annotationKey ?? string.Empty, out var key))
        {
            return false;
        }

        string[] names = [key.SubjectName, key.ResponsibilityName, key.ContextName, key.UnitName];

        return Contains(key.SubjectName, _subject)
            && Contains(key.ResponsibilityName, _responsibility)
            && Contains(key.ContextName, _context)
            && Contains(key.UnitName, _unit)
            && (_keyText is null || names.Any(name => Contains(name, _keyText)));
    }

    // Every value of the stored document that passes all content filters, in document order.
    public IEnumerable<SearchMatch> FindMatches(string annotationKey, long version, JObject content)
    {
        foreach (var (pointer, token, inDefinition) in Walk(content, string.Empty, inDefinition: false))
        {
            if (_property is not null && !MatchesProperty(pointer))
            {
                continue;
            }

            if (_valueText is not null && !(token is JValue value && value.Type != JTokenType.Null && ScalarText(value).Contains(_valueText, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (_bindsKey is not null && !Binds(token, _bindsKey))
            {
                continue;
            }

            var match = Describe(annotationKey, version, pointer, token);
            yield return _bindsKey is not null && inDefinition ? match with { Note = PossibleReadNote } : match;
        }
    }

    // Pages the listing and reads the stored documents within the budgets: five API pages, 200 document reads, at most
    // four at a time, and maxResults matches.
    public async Task<ConfigurationSearchResult> RunAsync(
        Func<string?, CancellationToken, Task<Sdk.ConfigurationsResponse>> listPage,
        Func<Sdk.ConfigurationSummary, CancellationToken, Task<JObject?>> readDocument,
        int maxResults,
        CancellationToken cancellationToken)
    {
        var matches = new List<SearchMatch>();
        var pages = 0;
        var reads = 0;
        string? continuationToken = null;
        string? truncatedBy = null;

        while (true)
        {
            var page = await listPage(continuationToken, cancellationToken);
            pages++;

            var candidates = (page.Configurations ?? []).Where(summary => MatchesKey(summary.AnnotationKey)).ToList();

            if (HasContentFilters)
            {
                var (stoppedBy, pageReads) = await SearchDocumentsAsync(candidates, readDocument, matches, maxResults, reads, cancellationToken);
                truncatedBy = stoppedBy;
                reads += pageReads;
            }
            else
            {
                truncatedBy = AddKeys(candidates, matches, maxResults);
            }

            continuationToken = page.ContinuationToken;

            if (truncatedBy is not null || string.IsNullOrEmpty(continuationToken))
            {
                break;
            }

            if (pages == MaxPages)
            {
                truncatedBy = ByPages;
                break;
            }
        }

        return new ConfigurationSearchResult(matches, truncatedBy is not null, truncatedBy, Explain(truncatedBy, maxResults), pages, reads);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool Contains(string name, string? filter)
    {
        return filter is null || (!string.IsNullOrEmpty(name) && name.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    private static string ScalarText(JValue value)
    {
        return value.Type == JTokenType.String ? value.Value<string>() ?? string.Empty : value.ToString(Formatting.None);
    }

    private static bool Binds(JToken token, string key)
    {
        return token is JValue { Type: JTokenType.String } value
            && BindingExpressions.IsBinding(value.Value<string>())
            && BindingExpressions.Parse(value.Value<string>()!).References.Any(reference =>
                reference.IsValuesStore && string.Equals(reference.Key, key, StringComparison.OrdinalIgnoreCase));
    }

    private static SearchMatch Describe(string annotationKey, long version, string pointer, JToken token)
    {
        if (token is JValue || token.ToString(Formatting.None).Length <= MaxInlineValueLength)
        {
            return new SearchMatch(annotationKey, version) { Pointer = pointer, Value = token.DeepClone() };
        }

        return new SearchMatch(annotationKey, version) { Pointer = pointer, ValueKind = token.Type == JTokenType.Array ? "array" : "object" };
    }

    // Every value with its pointer; inDefinition marks the program of an object binding, whose strings are bound only
    // when the program emits them (the resolver binds the program's result again).
    private static IEnumerable<(string Pointer, JToken Token, bool InDefinition)> Walk(JToken token, string pointer, bool inDefinition)
    {
        switch (token)
        {
            case JObject obj:
            {
                var isBinding = BindingScanner.IsObjectBinding(obj, out _, out _);

                foreach (var property in obj.Properties())
                {
                    var childPointer = JsonPointer.Append(pointer, property.Name);
                    var childInDefinition = inDefinition || (isBinding && property.Name == BindingScanner.DefinitionProperty);
                    yield return (childPointer, property.Value, childInDefinition);

                    foreach (var descendant in Walk(property.Value, childPointer, childInDefinition))
                    {
                        yield return descendant;
                    }
                }

                break;
            }

            case JArray array:
                for (var index = 0; index < array.Count; index++)
                {
                    var childPointer = JsonPointer.Append(pointer, index);
                    yield return (childPointer, array[index], inDefinition);

                    foreach (var descendant in Walk(array[index], childPointer, inDefinition))
                    {
                        yield return descendant;
                    }
                }

                break;
        }
    }

    private static string? AddKeys(IEnumerable<Sdk.ConfigurationSummary> candidates, List<SearchMatch> matches, int maxResults)
    {
        foreach (var summary in candidates)
        {
            if (matches.Count == maxResults)
            {
                return ByMaxResults;
            }

            matches.Add(new SearchMatch(summary.AnnotationKey, summary.Version));
        }

        return null;
    }

    private static string? Explain(string? truncatedBy, int maxResults)
    {
        return truncatedBy switch
        {
            ByPages => $"The search stopped after {MaxPages} API pages ({MaxPages * 1000:N0} configurations). Narrow it with annotationType or keyPrefix.",
            ByDocumentReads => $"The search stopped after reading {MaxDocumentReads} stored documents. Narrow it with annotationType, keyPrefix or the name filters.",
            ByMaxResults => $"The search stopped at {maxResults} matches. Raise maxResults (up to {MaxResultsLimit}) or narrow the search.",
            _ => null,
        };
    }

    private bool MatchesProperty(string pointer)
    {
        if (_property!.StartsWith('/'))
        {
            return pointer.EndsWith(_property, StringComparison.OrdinalIgnoreCase);
        }

        var segments = JsonPointer.Split(pointer);
        return segments.Count > 0 && string.Equals(segments[^1], _property, StringComparison.OrdinalIgnoreCase);
    }

    // Reads the documents of one page in batches of at most four, until a budget is used up; returns the reads it made.
    private async Task<(string? TruncatedBy, int Reads)> SearchDocumentsAsync(
        List<Sdk.ConfigurationSummary> candidates,
        Func<Sdk.ConfigurationSummary, CancellationToken, Task<JObject?>> readDocument,
        List<SearchMatch> matches,
        int maxResults,
        int readsBefore,
        CancellationToken cancellationToken)
    {
        // nothing is stored on version 0, and an empty document holds no value
        var stored = candidates.Where(summary => summary.Version > 0 && summary.HasContent).ToList();
        var reads = 0;
        var next = 0;

        while (next < stored.Count)
        {
            var left = MaxDocumentReads - readsBefore - reads;

            if (left <= 0)
            {
                return (ByDocumentReads, reads);
            }

            var batch = stored.Skip(next).Take(Math.Min(MaxConcurrentReads, left)).ToList();
            var documents = await Task.WhenAll(batch.Select(summary => readDocument(summary, cancellationToken)));
            reads += batch.Count;
            next += batch.Count;

            for (var index = 0; index < batch.Count; index++)
            {
                if (documents[index] is not { } document)
                {
                    continue;
                }

                foreach (var match in FindMatches(batch[index].AnnotationKey, batch[index].Version, document))
                {
                    if (matches.Count == maxResults)
                    {
                        return (ByMaxResults, reads);
                    }

                    matches.Add(match);
                }
            }
        }

        return (null, reads);
    }
}
