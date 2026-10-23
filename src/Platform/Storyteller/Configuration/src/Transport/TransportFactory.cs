using System.Security.Cryptography.X509Certificates;
using _42.Platform.Storyteller.Sdk.Configuring.Settings;
using _42.Platform.Storyteller.Sdk.Credentials;
using _42.Platform.Storyteller.Sdk.Resilience;
using Microsoft.Extensions.Http.Resilience;

namespace _42.Platform.Storyteller.Sdk.Configuring.Transport;

// The transport of the provider and of the document service, built by hand: the provider runs before the container
// exists, and a client of AddStorytellerSdk would collide with the application's own ISdkConfiguration and get a second
// resilience handler from Aspire's service defaults.
//
//   StorytellerCredentialHandler        Authorization, one renewal on a 401
//     └─ ResilienceHandler              retries of GET, circuit breaker, attempt timeout
//          └─ SocketsHttpHandler        client certificate, no redirects
internal static class TransportFactory
{
    // A host without TLS (a local Functions host) reads the certificate from this header, as sform sends it.
    internal const string LocalCertificateHeaderName = "X-ARR-ClientCert";

    private static readonly TimeSpan PooledConnectionLifetime = TimeSpan.FromMinutes(5);

    public static StorytellerTransport Create(StorytellerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var certificate = settings.Credential?.ClientCertificate;

        // A new pipeline per transport: it holds the state of its circuit breaker.
        HttpMessageHandler handler = new ResilienceHandler(StorytellerResilience.CreatePipeline(settings.Resilience))
        {
            InnerHandler = settings.PrimaryHandler is { } primary
                ? new NonDisposingHandler(primary)
                : CreatePrimaryHandler(settings.ConfigurePrimaryHandler, certificate),
        };

        if (settings.Credential is { } credential)
        {
            handler = new StorytellerCredentialHandler(credential, handler);
        }

        // ReadTimeout and the caller's token bound a whole run; attempt timeouts bound a request.
        var httpClient = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };

        // AccessTokenFactory stays null: the credential handler sets Authorization, and PrepareRequest would set it too.
        var client = new ConfigurationsApiClient(httpClient, new SdkConfiguration
        {
            BaseUrl = settings.Endpoint.AbsoluteUri,
            AccessTokenFactory = null,
            ClientCertificate = certificate,
            ClientCertificateHeaderName = certificate is not null && LocalUrl.IsLocalHttp(settings.Endpoint) ? LocalCertificateHeaderName : null,
        });

        return new StorytellerTransport(httpClient, client);
    }

    internal static SocketsHttpHandler CreatePrimaryHandler(Action<SocketsHttpHandler>? configure, X509Certificate2? certificate)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            PooledConnectionLifetime = PooledConnectionLifetime,
        };

        configure?.Invoke(handler);

        // Again after the callback: a proxy setting must drop neither.
        handler.AllowAutoRedirect = false;

        if (certificate is not null)
        {
            handler.SslOptions.ClientCertificates = [certificate];
        }

        return handler;
    }

    // The handler of a test belongs to the test, which may hand it to several transports in turn.
    private sealed class NonDisposingHandler : DelegatingHandler
    {
        public NonDisposingHandler(HttpMessageHandler innerHandler)
            : base(innerHandler)
        {
        }

        protected override void Dispose(bool disposing)
        {
            // The inner handler is not disposed.
        }
    }
}
