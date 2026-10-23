using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace _42.Platform.Storyteller.Sdk.Credentials;

// Sets Authorization from the credential. After a 401 it asks the credential once more, with the refused value as
// rejected, and sends the request again, so a token the API refused before its expiry does not end a long job.
public sealed class StorytellerCredentialHandler : DelegatingHandler
{
    private readonly StorytellerCredential _credential;

    public StorytellerCredentialHandler(StorytellerCredential credential)
    {
        ArgumentNullException.ThrowIfNull(credential);
        _credential = credential;
    }

    public StorytellerCredentialHandler(StorytellerCredential credential, HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
        ArgumentNullException.ThrowIfNull(credential);
        _credential = credential;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var authorization = await _credential.GetAuthorizationAsync(null, cancellationToken).ConfigureAwait(false);

        if (authorization is not null)
        {
            request.Headers.Authorization = authorization;
        }

        var requestUri = request.RequestUri;
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // Any other response is returned unread, so a streamed download passes through.
        if (response.StatusCode != HttpStatusCode.Unauthorized
            || authorization is null
            || !_credential.CanRenew
            || !CanSendAgain(request, requestUri, authorization))
        {
            return response;
        }

        AuthenticationHeaderValue? renewed;

        try
        {
            renewed = await _credential.GetAuthorizationAsync(authorization, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            response.Dispose();
            throw;
        }

        if (renewed is null || IsSame(renewed, authorization))
        {
            return response;
        }

        // The same message, as retry handlers do: its content is buffered, and the inner handler does not check
        // whether it was sent. The second response is returned whatever it is.
        response.Dispose();
        request.Headers.Authorization = renewed;
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    // A redirect rewrites RequestUri and clears Authorization on this message: the 401 then came from another host,
    // which must not get a fresh token. A stream belongs to the caller and may not rewind; StringContent and
    // FormUrlEncodedContent are ByteArrayContent.
    private static bool CanSendAgain(HttpRequestMessage request, Uri? requestUri, AuthenticationHeaderValue authorization)
    {
        return request.RequestUri == requestUri
            && request.Headers.Authorization is { } sent
            && IsSame(sent, authorization)
            && request.Content is null or ByteArrayContent;
    }

    private static bool IsSame(AuthenticationHeaderValue left, AuthenticationHeaderValue right)
    {
        return string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Parameter, right.Parameter, StringComparison.Ordinal);
    }
}
