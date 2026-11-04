using _42.Platform.Storyteller.Sdk.Configuring.Settings;
using _42.Platform.Storyteller.Sdk.Configuring.Transport;

namespace _42.Platform.Storyteller.Sdk.Hosting.Reading;

// The HttpClient of the catalog and the SDK's annotations client over it, with the provider's handlers: the credential
// of the Hosting settings, a circuit breaker of its own, and the primary handler. Disposing it closes the connections;
// a request still in flight then fails.
internal sealed class CatalogTransport : IDisposable
{
    private int _isDisposed;

    public CatalogTransport(StorytellerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        HttpClient = TransportFactory.CreateHttpClient(settings);
        Client = new AnnotationsApiClient(HttpClient, TransportFactory.CreateSdkConfiguration(settings));
    }

    public HttpClient HttpClient { get; }

    public IAnnotationsApiClient Client { get; }

    public bool IsDisposed => Volatile.Read(ref _isDisposed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 0)
        {
            HttpClient.Dispose();
        }
    }
}
