namespace _42.Platform.Storyteller.Mcp.Hosting;

// The MCP SDK writes whole JSON-RPC messages (tool arguments and results, secrets included) at Trace. Outside
// Development no rule may let a ModelContextProtocol category log below Information, whatever the configuration says:
// for every provider named in the rules (and for none) a rule for the category is added at Information, and rules that
// would win over it for an MCP category (a sub-category, a wildcard, a filter) are raised to Information.
public static class LoggingSafety
{
    public const string Category = "ModelContextProtocol";

    public static void Clamp(LoggerFilterOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var rules = options.Rules.Select(Raise).ToList();
        var providers = rules.Select(rule => rule.ProviderName).Append(null).Distinct(StringComparer.Ordinal).ToList();

        options.Rules.Clear();

        foreach (var rule in rules)
        {
            options.Rules.Add(rule);
        }

        foreach (var provider in providers)
        {
            options.Rules.Add(new LoggerFilterRule(provider, Category, LogLevel.Information, null));
        }
    }

    private static LoggerFilterRule Raise(LoggerFilterRule rule)
    {
        if (!MayMatchMcp(rule.CategoryName))
        {
            return rule;
        }

        var level = rule.LogLevel is { } configured && configured > LogLevel.Information ? configured : LogLevel.Information;
        var filter = rule.Filter is null
            ? null
            : new Func<string?, string?, LogLevel, bool>((provider, category, logLevel) => logLevel >= LogLevel.Information && rule.Filter(provider, category, logLevel));

        return new LoggerFilterRule(rule.ProviderName, rule.CategoryName, level, filter);
    }

    // A rule wins over the added one when its category is longer and still matches an MCP category: a sub-category, or
    // a wildcard whose prefix fits the MCP categories. The default rule (no category) and shorter prefixes lose anyway.
    private static bool MayMatchMcp(string? category)
    {
        if (string.IsNullOrEmpty(category))
        {
            return false;
        }

        var wildcard = category.IndexOf('*', StringComparison.Ordinal);
        var prefix = wildcard < 0 ? category : category[..wildcard];

        return wildcard < 0
            ? category.StartsWith(Category, StringComparison.OrdinalIgnoreCase)
            : prefix.StartsWith(Category, StringComparison.OrdinalIgnoreCase) || Category.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
