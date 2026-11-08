using System;
using Microsoft.Extensions.Configuration;

namespace _42.Platform.Cli.Configuration;

// The effective sections of the settings store as configuration (editor:editorType, access:projectName, …), so the
// options bind exactly what sform account settings lists. The store is created when the configuration loads, so a user
// folder that cannot be found fails nothing but the settings.
public sealed class UserSettingsConfigurationSource : IConfigurationSource
{
    private readonly Func<IUserSettingsStore> _createStore;
    private readonly Action<string>? _warn;

    public UserSettingsConfigurationSource(Func<IUserSettingsStore> createStore, Action<string>? warn = null)
    {
        _createStore = createStore;
        _warn = warn;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        return new UserSettingsConfigurationProvider(_createStore, _warn);
    }
}
