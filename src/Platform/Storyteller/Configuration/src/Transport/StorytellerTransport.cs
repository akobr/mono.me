namespace _42.Platform.Storyteller.Sdk.Configuring.Transport;

// The HttpClient of one engine or document service and the SDK client over it. Disposing it closes the connections;
// a request still in flight then fails with ObjectDisposedException.
internal sealed class StorytellerTransport : IDisposable
{
    public StorytellerTransport(HttpClient httpClient, IConfigurationsApiClient client)
    {
        HttpClient = httpClient;
        Client = client;
    }

    public HttpClient HttpClient { get; }

    public IConfigurationsApiClient Client { get; }

    public void Dispose()
    {
        HttpClient.Dispose();
    }
}
