using _42.Platform.Sdk.Client;

namespace _42.Platform.Sdk;

public interface IRestClientFactory
{
    IAsynchronousClient BuildAsynchronousClient(IReadableConfiguration configuration);

    ISynchronousClient BuildSynchronousClient(IReadableConfiguration configuration);
}
