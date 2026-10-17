using System;
using System.Security.Cryptography.X509Certificates;

namespace _42.Platform.Storyteller.Sdk;

public interface ISdkConfiguration
{
    string BaseUrl { get; }

    Func<string>? AccessTokenFactory { get; }

    /// <summary>
    /// Optional client certificate for mTLS authentication.
    /// When set, the <see cref="System.Net.Http.HttpClientHandler"/> is configured
    /// with this certificate for mutual TLS.
    /// </summary>
    X509Certificate2? ClientCertificate { get; }

    /// <summary>
    /// Development only: when set together with <see cref="ClientCertificate"/>, every request carries the
    /// certificate (base64 DER) in this header, for hosts without TLS, e.g. <c>X-ARR-ClientCert</c> of a local
    /// Functions host that allows it (<c>MachineAuth:IsCertificateHeaderFromClientAllowed</c>).
    /// </summary>
    string? ClientCertificateHeaderName => null;
}

public class SdkConfiguration : ISdkConfiguration
{
    public string BaseUrl { get; set; } = "https://api.42.com";

    public Func<string>? AccessTokenFactory { get; set; }

    public X509Certificate2? ClientCertificate { get; set; }

    public string? ClientCertificateHeaderName { get; set; }
}
