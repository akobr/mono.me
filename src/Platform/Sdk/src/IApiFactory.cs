using _42.Platform.Sdk.Api;

namespace _42.Platform.Sdk;

public interface IApiFactory
{
    IAnnotationsApi GetAnnotationsApi();

    IConfigurationApi GetConfigurationApi();

    IAccessApi GetAccessApi();
}
