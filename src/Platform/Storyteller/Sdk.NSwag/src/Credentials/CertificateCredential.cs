using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

namespace _42.Platform.Storyteller.Sdk.Credentials;

// The certificate goes in the TLS handshake; no Authorization header.
internal sealed class CertificateCredential : StorytellerCredential
{
    private readonly X509Certificate2 _certificate;

    public CertificateCredential(X509Certificate2 certificate)
    {
        _certificate = certificate;
    }

    public override X509Certificate2? ClientCertificate => _certificate;

    public override ValueTask<AuthenticationHeaderValue?> GetAuthorizationAsync(AuthenticationHeaderValue? rejected, CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<AuthenticationHeaderValue?>(null);
    }

    public override string ToString()
    {
        return $"{nameof(StorytellerCredential)} {{ Kind = Certificate, Certificate = {_certificate.Thumbprint} }}";
    }
}
