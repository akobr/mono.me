using Microsoft.Extensions.Configuration;

namespace _42.Utils.Configuration.Substitute;

public class SubstitutableConfigurationSection(
    IConfigurationSection wrappedSection,
    ISubstitutionExecutor? executor)
    : SubstitutableConfiguration(wrappedSection, executor), IConfigurationSection
{
    public string Key => wrappedSection.Key;

    public string Path => wrappedSection.Path;

    public string? Value
    {
        get => GetConfigurationValue(Key, wrappedSection.Value);
        set => wrappedSection.Value = value;
    }
}
