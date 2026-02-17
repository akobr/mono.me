using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace _42.Platform.Storyteller.Binding;

public class BindingService : IBindingExecutor, IBindingRegistry
{
    public const string DefaultBindingKey = "default";
    private static readonly Regex BindingRegex = new(
        @"^\@((?<path>[\w.]+)|\((?<pathSourced>[\w.]+)\,\s*(?<source>\w+)\)|(?<word>\w+)\((?<param>.+)\))$",
        RegexOptions.Compiled);

    private readonly ConcurrentDictionary<string, IBindingStrategy> _strategies = new(StringComparer.Ordinal);

    public ValueTask<bool> TryBinding(JProperty property, bool includeSecrets)
    {
        if (property.Type != JTokenType.String)
        {
            return ValueTask.FromResult(false);
        }

        var rawValue = (string)property.Value;

        if (rawValue[0] != '@')
        {
            return ValueTask.FromResult(false);
        }

        var match = BindingRegex.Match(rawValue);

        if (!match.Success)
        {
            return ValueTask.FromResult(false);
        }

        if (match.Groups.ContainsKey("word")) // with word (path and pointer)
        {
            // TODO: [P3] add support for word-based data binding
            // filtering and selection
            // @path(@.price<\$.maxPrice)
            // @pointer(/objects/and/3/arrays)
            return ValueTask.FromResult(false);
        }

        BindingContext context = match.Groups.TryGetValue("path", out var path)
            ? new(property, includeSecrets) { BindingKey = DefaultBindingKey, Path = path.Value }
            : new(property, includeSecrets) { BindingKey = match.Groups["source"].Value, Path = match.Groups["pathSourced"].Value };

        return _strategies.TryGetValue(context.BindingKey, out var strategy)
            ? strategy.TryBinding(context)
            : ValueTask.FromResult(false);
    }

    public void RegisterStrategy(string key, IBindingStrategy strategy)
    {
        _strategies[key] = strategy;
    }
}
