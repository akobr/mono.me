using _42.Platform.Sdk.Client;

namespace _42.Platform.Sdk;

public interface IConfigurationFactory
{
    IReadableConfiguration BuildConfiguration();
}
