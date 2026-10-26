using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace _42.Platform.Storyteller.Mcp.UnitTests.Support;

// PKCS#12 files of self-signed certificates for the actor's client assertion key, made once per test run.
internal static class TestCertificates
{
    public const string Password = "actor-key-password";

    private static readonly Lazy<(byte[] Pkcs12, RSAParameters PublicKey)> Rsa2048 = new(() => CreateRsa(2048));
    private static readonly Lazy<byte[]> Rsa1024 = new(() => CreateRsa(1024).Pkcs12);
    private static readonly Lazy<byte[]> EllipticCurve = new(CreateEllipticCurve);

    public static byte[] RsaPkcs12 => Rsa2048.Value.Pkcs12;

    public static RSAParameters RsaPublicKey => Rsa2048.Value.PublicKey;

    public static byte[] SmallRsaPkcs12 => Rsa1024.Value;

    public static byte[] EllipticCurvePkcs12 => EllipticCurve.Value;

    private static (byte[] Pkcs12, RSAParameters PublicKey) CreateRsa(int keySize)
    {
        using var rsa = RSA.Create(keySize);
        var request = new CertificateRequest("CN=mcp-actor", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

        return (certificate.Export(X509ContentType.Pkcs12, Password), rsa.ExportParameters(includePrivateParameters: false));
    }

    private static byte[] CreateEllipticCurve()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=mcp-actor", ecdsa, HashAlgorithmName.SHA256);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

        return certificate.Export(X509ContentType.Pkcs12, Password);
    }
}
