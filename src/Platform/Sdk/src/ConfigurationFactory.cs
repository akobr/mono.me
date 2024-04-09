using System;
using _42.Platform.Sdk.Client;

namespace _42.Platform.Sdk;

public class ConfigurationFactory : IConfigurationFactory
{
    private readonly Lazy<IReadableConfiguration> _configuration;

    public ConfigurationFactory(Func<IReadableConfiguration> configurationFactory)
    {
        _configuration = new Lazy<IReadableConfiguration>(configurationFactory);
    }

    public IReadableConfiguration BuildConfiguration()
    {
        return _configuration.Value;
    }
}
