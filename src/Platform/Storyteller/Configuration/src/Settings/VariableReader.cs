namespace _42.Platform.Storyteller.Sdk.Configuring.Settings;

// Reads STORYTELLER_* values: trimmed, an empty value counts as unset, and a name and its sform fallback must agree.
internal sealed class VariableReader
{
    private readonly Func<string, string?> _getVariable;

    public VariableReader(Func<string, string?> getVariable)
    {
        _getVariable = getVariable;
    }

    public Func<string, string?> GetVariable => _getVariable;

    public string? Read(string name)
    {
        var value = _getVariable(name)?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    // source is the name the value came from, for messages.
    public string? ReadWithFallback(string name, string fallback, out string source)
    {
        var value = Read(name);
        var fallbackValue = Read(fallback);

        if (value is not null && fallbackValue is not null && !string.Equals(value, fallbackValue, StringComparison.Ordinal))
        {
            throw new StorytellerConfigurationException($"{name} and {fallback} are both set, to different values; set one of them.");
        }

        source = value is null && fallbackValue is not null ? fallback : name;
        return value ?? fallbackValue;
    }
}
