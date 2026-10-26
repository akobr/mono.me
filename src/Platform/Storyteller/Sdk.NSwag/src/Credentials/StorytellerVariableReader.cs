using System;
using System.Collections.Generic;

namespace _42.Platform.Storyteller.Sdk.Credentials;

// Reads STORYTELLER_* values: trimmed, an empty value counts as unset, and a name and its deprecated name must agree.
// Remembers the names it found set, never their values; messages name variables, never values.
public sealed class StorytellerVariableReader
{
    private readonly Func<string, string?> _getVariable;
    private readonly List<string> _setNames = [];

    // The process environment when getVariable is null.
    public StorytellerVariableReader(Func<string, string?>? getVariable = null)
    {
        _getVariable = getVariable ?? Environment.GetEnvironmentVariable;
    }

    // The names read so far that were set, in the order they were read.
    public IReadOnlyList<string> SetNames => _setNames.AsReadOnly();

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

    // fallback is the deprecated name, read when name is unset; source is the name the value came from, for messages.
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
