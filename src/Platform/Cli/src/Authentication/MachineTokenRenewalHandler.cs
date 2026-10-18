using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace _42.Platform.Cli.Authentication;

// On the typed clients of the Storyteller SDK: after a 401, a request is sent once more with the token of a new
// exchange, so a federated token the API refused before its expiry does not end a long job. Every other kind
// gets the 401 as it is.
public sealed class MachineTokenRenewalHandler : DelegatingHandler
{
    private const string BearerScheme = "Bearer";

    private readonly IMachineSignIn _machine;

    public MachineTokenRenewalHandler(IMachineSignIn machine)
    {
        _machine = machine;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestUri = request.RequestUri;
        var rejectedToken = ReadBearerToken(request);
        var response = await base.SendAsync(request, cancellationToken);

        // Any other response is returned unread, so a streamed download passes through.
        if (response.StatusCode != HttpStatusCode.Unauthorized || rejectedToken is null || !CanSendAgain(request, requestUri, rejectedToken))
        {
            return response;
        }

        string? token;

        try
        {
            // No cancellation, like the token factory: the timeout of the exchange client ends a hanging exchange.
            token = await _machine.RenewAccessTokenAsync(rejectedToken, CancellationToken.None);
        }
        catch
        {
            response.Dispose();
            throw;
        }

        if (token is null)
        {
            return response;
        }

        // The same message, as retry handlers do: its content is buffered, and the inner handler does not check
        // whether it was sent. The second response is returned whatever it is.
        response.Dispose();
        request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, token);
        return await base.SendAsync(request, cancellationToken);
    }

    private static string? ReadBearerToken(HttpRequestMessage request)
    {
        return request.Headers.Authorization is { Parameter: { Length: > 0 } token } authorization
            && string.Equals(authorization.Scheme, BearerScheme, StringComparison.OrdinalIgnoreCase)
                ? token
                : null;
    }

    private bool CanSendAgain(HttpRequestMessage request, Uri? requestUri, string rejectedToken)
    {
        // A redirect rewrites RequestUri and clears Authorization on this message: the 401 then came from another
        // host, which must not get a fresh Storyteller token. A stream belongs to the caller and may not rewind;
        // StringContent and FormUrlEncodedContent are ByteArrayContent.
        return _machine.IsConfigured
            && request.RequestUri == requestUri
            && string.Equals(ReadBearerToken(request), rejectedToken, StringComparison.Ordinal)
            && request.Content is null or ByteArrayContent;
    }
}
