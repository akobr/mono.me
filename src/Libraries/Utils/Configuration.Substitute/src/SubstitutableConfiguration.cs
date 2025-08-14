using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace _42.Utils.Configuration.Substitute;

public class SubstitutableConfiguration(
    IConfiguration wrappedConfiguration,
    ISubstitutionExecutor? executor)
    : ISubstitutableConfiguration
{
    public string? this[string key]
    {
        get => GetConfigurationValue(key);
        set => wrappedConfiguration[key] = value;
    }

    public IConfigurationSection GetSection(string key)
    {
        return new SubstitutableConfigurationSection(wrappedConfiguration.GetSection(key), executor);
    }

    public IEnumerable<IConfigurationSection> GetChildren()
    {
        return wrappedConfiguration.GetChildren().Select(s => new SubstitutableConfigurationSection(s, executor));
    }

    public IChangeToken GetReloadToken()
    {
        return wrappedConfiguration.GetReloadToken();
    }

    protected string? GetConfigurationValue(string key, string? originalValue)
    {
        if (originalValue is null)
        {
            return null;
        }

        if (executor is null
            || !originalValue.StartsWith('@'))
        {
            return originalValue;
        }

        if (originalValue.StartsWith("@@"))
        {
            return originalValue[1..];
        }

        if (executor.TrySubstitute(key, originalValue, out var resultValue))
        {
            return resultValue;
        }

        return originalValue;
    }

    private string? GetConfigurationValue(string key)
    {
        return GetConfigurationValue(key, wrappedConfiguration[key]);
    }
}
