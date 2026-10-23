using System;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace _42.Platform.Storyteller.Sdk.Credentials;

internal sealed class ApiKeyCredential : StorytellerCredential
{
    private const string Scheme = "ApiKey";

    private readonly AuthenticationHeaderValue _authorization;
    private readonly X509Certificate2? _certificate;

    public ApiKeyCredential(string apiKey, X509Certificate2? certificate)
    {
        _authorization = new AuthenticationHeaderValue(Scheme, Normalize(apiKey));
        _certificate = certificate;
    }

    public override X509Certificate2? ClientCertificate => _certificate;

    public override ValueTask<AuthenticationHeaderValue?> GetAuthorizationAsync(AuthenticationHeaderValue? rejected, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<AuthenticationHeaderValue?>(_authorization);
    }

    public override string ToString()
    {
        return _certificate is null
            ? $"{nameof(StorytellerCredential)} {{ Kind = ApiKey }}"
            : $"{nameof(StorytellerCredential)} {{ Kind = ApiKey, Certificate = {_certificate.Thumbprint} }}";
    }

    internal ApiKeyCredential WithCertificate(X509Certificate2 certificate)
    {
        return new ApiKeyCredential(_authorization.Parameter!, certificate);
    }

    // The scheme of the Authorization header is accepted too, as curl snippets show it.
    private static string Normalize(string apiKey)
    {
        ArgumentNullException.ThrowIfNull(apiKey);

        var key = apiKey.Trim();

        if (key.StartsWith(Scheme, StringComparison.OrdinalIgnoreCase)
            && (key.Length == Scheme.Length || char.IsWhiteSpace(key[Scheme.Length])))
        {
            key = key[Scheme.Length..].Trim();
        }

        return key.Length > 0
            ? key
            : throw new ArgumentException("The API key is empty.", nameof(apiKey));
    }
}
