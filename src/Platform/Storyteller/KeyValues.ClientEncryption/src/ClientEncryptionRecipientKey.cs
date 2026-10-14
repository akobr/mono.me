using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace _42.Platform.Storyteller.KeyValues.ClientEncryption;

// A recipient's RSA public key as a JWK (RFC 7517): the form the platform stores and the clients encrypt for.
public sealed record RsaPublicJwk(string N, string E)
{
    public const int MinimumModulusBits = 3072;

    public string Kty => "RSA";

    public int ModulusBits => Base64Url.DecodeFromChars(N).Length * 8;

    public static RsaPublicJwk FromRsa(RSA rsa)
    {
        ArgumentNullException.ThrowIfNull(rsa);
        var parameters = rsa.ExportParameters(includePrivateParameters: false);
        return new RsaPublicJwk(Base64Url.EncodeToString(parameters.Modulus!), Base64Url.EncodeToString(parameters.Exponent!));
    }

    // A PEM public key ("-----BEGIN PUBLIC KEY-----") or a JWK JSON object.
    public static RsaPublicJwk Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        var trimmed = text.Trim();

        if (trimmed.StartsWith('{'))
        {
            JsonObject? json;

            try
            {
                json = JsonNode.Parse(trimmed) as JsonObject;
            }
            catch (JsonException exception)
            {
                throw new FormatException("The public key is neither PEM nor a JWK object.", exception);
            }

            var kty = AsString(json?["kty"]);
            var n = AsString(json?["n"]);
            var e = AsString(json?["e"]);

            if (!string.Equals(kty, "RSA", StringComparison.Ordinal) || string.IsNullOrEmpty(n) || string.IsNullOrEmpty(e))
            {
                throw new FormatException("The JWK must be an RSA public key with 'n' and 'e'.");
            }

            return new RsaPublicJwk(n, e);
        }

        using var rsa = RSA.Create();

        try
        {
            rsa.ImportFromPem(trimmed);
        }
        catch (Exception exception) when (exception is ArgumentException or CryptographicException)
        {
            throw new FormatException("The public key is neither PEM nor a JWK object.", exception);
        }

        return FromRsa(rsa);
    }

    public RSA ToRsa()
    {
        var rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters
        {
            Modulus = Base64Url.DecodeFromChars(N),
            Exponent = Base64Url.DecodeFromChars(E),
        });
        return rsa;
    }

    public string ToJson()
    {
        return new JsonObject { ["kty"] = Kty, ["n"] = N, ["e"] = E }.ToJsonString();
    }

    private static string? AsString(JsonNode? node)
    {
        return node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }
}

// What a client encrypts for: the recipient id (the JWE kid) and its public key.
public sealed record ClientEncryptionRecipientKey(string Id, RsaPublicJwk PublicKey);
