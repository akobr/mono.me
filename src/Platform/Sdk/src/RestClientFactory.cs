using _42.Platform.Sdk.Client;

namespace _42.Platform.Sdk;

public class RestClientFactory : IRestClientFactory
{
    public IAsynchronousClient BuildAsynchronousClient(IReadableConfiguration configuration)
    {
        return BuildApiClient(configuration);
    }

    public ISynchronousClient BuildSynchronousClient(IReadableConfiguration configuration)
    {
        return BuildApiClient(configuration);
    }

    private static ApiClient BuildApiClient(IReadableConfiguration configuration)
    {
        return new ApiClient(configuration.BasePath);
    }
}
