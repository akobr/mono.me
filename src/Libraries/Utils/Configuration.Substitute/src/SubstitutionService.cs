using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using _42.Utils.Configuration.Substitute.Aggregate;

namespace _42.Utils.Configuration.Substitute;

public class SubstitutionService : ISubstitutionExecutor, ISubstitutionRegistry
{
    private const string DefaultSourceKey = "default";
    private static readonly Regex SubstituteRegex = new(@"\@((?<sprop>[\w.\-]+)|(\((?<prop>[\w.\-]+),\s*(?<key>[\w\-]+)\)))", RegexOptions.Compiled);

    private readonly ConcurrentDictionary<string, ISubstituteStrategy> _strategies = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _substitutionCache = new(StringComparer.OrdinalIgnoreCase);

    public void RegisterSubstitution(string? sourceKey, ISubstituteStrategy strategy)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
        {
            sourceKey = DefaultSourceKey;
        }

        if (!_strategies.TryGetValue(sourceKey, out var existingStrategy))
        {
            _strategies[sourceKey] = strategy;
            return;
        }

        _strategies[sourceKey] = existingStrategy.AggregateWith(strategy);
    }

    public bool TrySubstitute(string key, string value, [MaybeNullWhen(false)] out string newValue)
    {
        var match = SubstituteRegex.Match(value);

        if (!match.Success)
        {
            newValue = value;
            return false;
        }

        if (_substitutionCache.TryGetValue(value, out newValue))
        {
            return true;
        }

        var sourceKey = match.Groups.Count > 3
            ? match.Groups["key"].Value
            : DefaultSourceKey;

        if (!_strategies.TryGetValue(sourceKey, out var strategy))
        {
            newValue = value;
            return false;
        }

        var property = match.Groups[match.Groups.Count > 3 ? "prop" : "sprop"].Value;
        var substitute = strategy.Substitute(property);

        if (substitute is null)
        {
            newValue = value;
            _substitutionCache[value] = value;
            return false;
        }

        newValue = substitute;
        _substitutionCache[value] = newValue;
        return true;
    }
}
