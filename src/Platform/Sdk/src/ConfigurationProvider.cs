using System;
using _42.Platform.Sdk.Client;

namespace _42.Platform.Sdk;

public class ConfigurationProvider : IConfigurationProvider
{
    Lazy<Configuration> _configuration;

    public ConfigurationProvider(Func<Configuration> configurationFactory)
    {
        _configuration = new Lazy<Configuration>(configurationFactory);
    }

    public Configuration GetConfiguration()
    {
        return _configuration.Value;
    }
}
