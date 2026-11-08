using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace _42.Platform.Cli.Services;

// Remembers the address of the request a client of sform's IHttpClientFactory sends last, so that a request that fails
// without an answer (a refused connection, a name that does not resolve, a timeout) can be reported with the service it
// was meant for: the API, or the sign-in service and its discovery. Registered for every client (Startup); sform sends
// its requests one after the other, so the last one started is the one that failed.
public sealed class RequestTargetHandler : DelegatingHandler
{
    private static string? _lastTarget;

    // The scheme, host and port of the last request sent; null before the first.
    public static string? LastTarget => Volatile.Read(ref _lastTarget);

    internal static void Reset()
    {
        Volatile.Write(ref _lastTarget, null);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.RequestUri is { IsAbsoluteUri: true } uri)
        {
            Volatile.Write(ref _lastTarget, uri.GetLeftPart(UriPartial.Authority));
        }

        return base.SendAsync(request, cancellationToken);
    }
}
