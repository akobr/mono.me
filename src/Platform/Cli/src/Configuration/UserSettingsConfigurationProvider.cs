using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace _42.Platform.Cli.Configuration;

public sealed class UserSettingsConfigurationProvider : ConfigurationProvider
{
    private readonly Func<IUserSettingsStore> _createStore;
    private readonly Action<string>? _warn;

    public UserSettingsConfigurationProvider(Func<IUserSettingsStore> createStore, Action<string>? warn)
    {
        _createStore = createStore;
        _warn = warn;
    }

    // Only the known, valid properties: a value written by hand that sform cannot use is left out instead of failing the
    // commands that bind it. Nothing here stops sform, which runs in pipelines that never use the settings: a damaged file
    // is reported and gives no settings (not the legacy files it replaced, whose values may be stale), and so does any
    // other failure, e.g. a user folder that cannot be found.
    public override void Load()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        Data = data;

        UserSettingsSnapshot snapshot;

        try
        {
            snapshot = _createStore().Read();
        }
        catch (UserSettingsException exception) when (exception.IsDamaged)
        {
            _warn?.Invoke($"{exception.Message} sform uses no editor and no default project from it until it is fixed (sform account settings --path).");
            return;
        }
        catch (Exception exception)
        {
            _warn?.Invoke($"The settings of sform cannot be read: {exception.Message}");
            return;
        }

        foreach (var section in snapshot.Sections)
        {
            foreach (var property in section.Values.Properties())
            {
                data[ConfigurationPath.Combine(section.Name, property.Name)] = property.Value.ToString();
            }
        }
    }
}
