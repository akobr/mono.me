using System;
using System.Collections.Generic;

namespace _42.Platform.Storyteller.Sdk.Credentials;

// Reads STORYTELLER_* values: trimmed, an empty value counts as unset, and a name and its sform fallback must agree.
// Remembers the names it found set, never their values.
internal sealed class EnvironmentVariables
{
    private readonly Func<string, string?> _getVariable;
    private readonly List<string> _setNames = [];

    public EnvironmentVariables(Func<string, string?> getVariable)
    {
        _getVariable = getVariable;
    }

    public IReadOnlyList<string> SetNames => _setNames;

    public string? Read(string name)
    {
        var value = _getVariable(name)?.Trim();

        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        _setNames.Add(name);
        return value;
    }

    // source is the name the value came from, for messages.
    public string? ReadWithFallback(string name, string fallback, out string source)
    {
        var value = Read(name);
        var fallbackValue = Read(fallback);

        if (value is not null && fallbackValue is not null && !string.Equals(value, fallbackValue, StringComparison.Ordinal))
        {
            throw StorytellerAuthenticationException.Configuration($"{name} and {fallback} are both set, to different values; set one of them.");
        }

        source = value is null && fallbackValue is not null ? fallback : name;
        return value ?? fallbackValue;
    }
}
