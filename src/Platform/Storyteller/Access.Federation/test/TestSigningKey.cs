using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace _42.Platform.Storyteller.Access.Federation.UnitTests;

// A locally generated RSA or EC key that signs platform tokens and publishes itself as a JWK.
internal sealed class TestSigningKey : IDisposable
{
    private static readonly JsonWebTokenHandler Handler = new();

    private readonly RSA? _rsa;
    private readonly ECDsa? _ecdsa;

    private TestSigningKey(string kid, RSA? rsa, ECDsa? ecdsa)
    {
        Kid = kid;
        _rsa = rsa;
        _ecdsa = ecdsa;
    }

    public string Kid { get; }

    // The private key, named by Kid.
    public SecurityKey SecurityKey => _rsa is not null
        ? new RsaSecurityKey(_rsa) { KeyId = Kid }
        : new ECDsaSecurityKey(_ecdsa!) { KeyId = Kid };

    public static TestSigningKey Rsa(string kid) => new(kid, RSA.Create(2048), null);

    public static TestSigningKey EllipticCurve(string kid) => new(kid, null, ECDsa.Create(ECCurve.NamedCurves.nistP256));

    // A JSON Web Key Set with the public halves of the keys.
    public static string Jwks(params TestSigningKey[] keys)
    {
        return JsonSerializer.Serialize(new { keys = keys.Select(key => key.ToJwk()).ToArray() });
    }

    // Signs the payload with this key; kid null leaves the header without one.
    public string Sign(object payload, string? algorithm = null, string? kid = "")
    {
        var keyId = kid == string.Empty ? Kid : kid;
        SecurityKey key = _rsa is not null
            ? new RsaSecurityKey(_rsa) { KeyId = keyId }
            : new ECDsaSecurityKey(_ecdsa!) { KeyId = keyId };
        algorithm ??= _rsa is not null ? SecurityAlgorithms.RsaSha256 : SecurityAlgorithms.EcdsaSha256;

        return Handler.CreateToken(JsonSerializer.Serialize(payload), new SigningCredentials(key, algorithm));
    }

    // A token whose parts are written as given (alg none, a wrong typ).
    public static string Unsigned(object header, object payload, string signature = "")
    {
        return $"{Encode(header)}.{Encode(payload)}.{signature}";
    }

    public static string Encode(object value)
    {
        return Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)));
    }

    public void Dispose()
    {
        _rsa?.Dispose();
        _ecdsa?.Dispose();
    }

    private Dictionary<string, string> ToJwk()
    {
        if (_rsa is not null)
        {
            var parameters = _rsa.ExportParameters(includePrivateParameters: false);
            return new Dictionary<string, string>
            {
                ["kty"] = "RSA",
                ["use"] = "sig",
                ["kid"] = Kid,
                ["n"] = Base64UrlEncoder.Encode(parameters.Modulus!),
                ["e"] = Base64UrlEncoder.Encode(parameters.Exponent!),
            };
        }

        var point = _ecdsa!.ExportParameters(includePrivateParameters: false).Q;
        return new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["use"] = "sig",
            ["kid"] = Kid,
            ["crv"] = "P-256",
            ["x"] = Base64UrlEncoder.Encode(point.X!),
            ["y"] = Base64UrlEncoder.Encode(point.Y!),
        };
    }
}
