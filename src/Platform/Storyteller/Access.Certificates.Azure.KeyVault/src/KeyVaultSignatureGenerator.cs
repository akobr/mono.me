using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure.Security.KeyVault.Keys.Cryptography;

namespace _42.Platform.Storyteller;

/// <summary>
/// An <see cref="X509SignatureGenerator"/> that delegates signing to Azure Key Vault
/// via <see cref="CryptographyClient"/>. The private key never leaves the vault.
/// <para>
/// This generator must only be used on the issuance/renewal path — never on the
/// request validation hot path. The validator uses only the CA's cached public
/// certificate.
/// </para>
/// </summary>
public class KeyVaultSignatureGenerator : X509SignatureGenerator
{
    private readonly CryptographyClient _cryptoClient;
    private readonly PublicKey _publicKey;

    public KeyVaultSignatureGenerator(CryptographyClient cryptoClient, PublicKey publicKey)
    {
        _cryptoClient = cryptoClient ?? throw new ArgumentNullException(nameof(cryptoClient));
        _publicKey = publicKey ?? throw new ArgumentNullException(nameof(publicKey));
    }

    /// <summary>
    /// Returns the DER-encoded <c>AlgorithmIdentifier</c> for RSA PKCS#1 with the given hash.
    /// Delegates to a throwaway local RSA generator to avoid hand-rolling ASN.1.
    /// </summary>
    public override byte[] GetSignatureAlgorithmIdentifier(HashAlgorithmName hashAlgorithm)
    {
        using var tempKey = RSA.Create(2048);
        var tempGenerator = CreateForRSA(tempKey, RSASignaturePadding.Pkcs1);
        return tempGenerator.GetSignatureAlgorithmIdentifier(hashAlgorithm);
    }

    /// <summary>
    /// Hashes <paramref name="data"/> locally, then calls <see cref="CryptographyClient.Sign"/>
    /// synchronously. This is a blocking network call, acceptable only on the issuance path.
    /// </summary>
    public override byte[] SignData(byte[] data, HashAlgorithmName hashAlgorithm)
    {
        byte[] digest;
        using (var hasher = IncrementalHash.CreateHash(hashAlgorithm))
        {
            hasher.AppendData(data);
            digest = hasher.GetHashAndReset();
        }

        var algorithm = hashAlgorithm.Name switch
        {
            "SHA256" => SignatureAlgorithm.RS256,
            "SHA384" => SignatureAlgorithm.RS384,
            "SHA512" => SignatureAlgorithm.RS512,
            _ => throw new ArgumentException($"Unsupported hash algorithm: {hashAlgorithm.Name}"),
        };

        var result = _cryptoClient.Sign(algorithm, digest);
        return result.Signature;
    }

    protected override PublicKey BuildPublicKey()
    {
        return _publicKey;
    }
}
