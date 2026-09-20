using System.Security.Cryptography.X509Certificates;

namespace _42.Platform.Storyteller.Accessing.Model;

public record CertificateAuthorityMaterial(
    X509Certificate2 Certificate,
    X509SignatureGenerator SignatureGenerator);
