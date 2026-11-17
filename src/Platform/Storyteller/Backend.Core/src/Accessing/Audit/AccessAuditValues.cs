using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using _42.Platform.Storyteller.Accessing.Model;

namespace _42.Platform.Storyteller.Accessing;

// The old and new values of the access audit records: a role name, a description, or compact JSON of what decides access.
// Never a secret: no API key, client secret, certificate, password, hash or stored mask (it keeps a client secret's first
// characters), no JWKS key (only their number and a thumbprint of the set) and no recipient's public key (only its
// thumbprint).
public static class AccessAuditValues
{
    // A longer value is cut, and ends with the start of its SHA-256, so a full copy can still be matched.
    public const int MaxLength = 4000;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string? Role(AccountRole? role)
    {
        return role is null or AccountRole.None ? null : role.Value.ToString();
    }

    public static string? Text(string? value)
    {
        return value is null ? null : Cap(value);
    }

    public static string Json(object value)
    {
        return Cap(JsonSerializer.Serialize(value, SerializerOptions));
    }

    public static string Machine(
        MachineCredentialKind credentialKind,
        IEnumerable<MachineScope>? scopes,
        string? annotationKey,
        bool usesSharedCertificate,
        string? certificateThumbprint)
    {
        return Json(new
        {
            CredentialKind = credentialKind,
            Scopes = scopes?.ToList() ?? [],
            AnnotationKey = string.IsNullOrEmpty(annotationKey) ? null : annotationKey,
            UsesSharedCertificate = usesSharedCertificate ? true : (bool?)null,
            CertificateThumbprint = certificateThumbprint,
        });
    }

    public static string FederationRules(IReadOnlyList<FederationRule>? rules)
    {
        return Json(rules ?? []);
    }

    public static string MachineAuthentication(MachineAuthenticationPolicy? policy)
    {
        return Json(policy ?? new MachineAuthenticationPolicy());
    }

    // What of a key/value policy decides who gets secrets and how; key versions, health and rewrap progress are left out,
    // so the maintenance runs, a verification and a rotation change no view.
    public static string KeyValuePolicy(KeyValuePolicy policy)
    {
        return Json(new
        {
            // A set: the order the levels were sent in is no change.
            AllowedProtections = policy.AllowedProtections.Distinct().Order().ToList(),
            policy.DefaultSecretProtection,
            policy.RequireMtlsForSecretDelivery,
            HsmBackend = policy.Hsm?.Backend,
            HsmRotationInterval = policy.Hsm?.RotationInterval,
            ByokKeyUri = policy.Byok?.KeyUri,
            ByokTenantId = policy.Byok?.TenantId,
            Recipients = policy.Recipients
                .Select(recipient => new
                {
                    recipient.Id,
                    recipient.Name,
                    recipient.IsEnabled,
                    KeyThumbprint = Thumbprint($"{recipient.PublicKey.Kty}.{recipient.PublicKey.N}.{recipient.PublicKey.E}"),
                })
                .ToList(),
        });
    }

    public static string Issuer(FederatedIssuer issuer)
    {
        return Json(new
        {
            issuer.Issuer,
            issuer.KeySource,
            issuer.JwksUri,
            JwksKeys = CountJwksKeys(issuer.Jwks),
            JwksThumbprint = JwksThumbprint(issuer.Jwks),
            issuer.MaxTokenLifetimeSeconds,
            issuer.RequiredClaims,
            issuer.Description,
        });
    }

    public static string Certificate(SharedCertificate certificate)
    {
        return Json(new
        {
            certificate.Label,
            certificate.NotBefore,
            certificate.NotAfter,
            IsRevoked = certificate.IsRevoked ? true : (bool?)null,
        });
    }

    public static string Cap(string value)
    {
        if (value.Length <= MaxLength)
        {
            return value;
        }

        var suffix = $"… (truncated, sha256 {Thumbprint(value)})";
        return string.Concat(value.AsSpan(0, MaxLength - suffix.Length), suffix);
    }

    // The first 16 hexadecimal digits of the SHA-256 of the text.
    private static string Thumbprint(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash, 0, 8).ToLower(CultureInfo.InvariantCulture);
    }

    // A thumbprint of the key set in a canonical form (compact, the members of every object by name, arrays in their
    // order), so a key replaced by another (the same number of keys) is a change of the issuer, and a key set only
    // formatted or ordered differently is none.
    private static string? JwksThumbprint(string? jwks)
    {
        if (string.IsNullOrWhiteSpace(jwks))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(jwks);
            return Thumbprint(Canonical(document.RootElement));
        }
        catch (JsonException)
        {
            return Thumbprint(jwks);
        }
    }

    private static string Canonical(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => "{" + string.Join(
                ",",
                element.EnumerateObject()
                    .OrderBy(member => member.Name, StringComparer.Ordinal)
                    .Select(member => $"{JsonSerializer.Serialize(member.Name)}:{Canonical(member.Value)}")) + "}",
            JsonValueKind.Array => "[" + string.Join(",", element.EnumerateArray().Select(Canonical)) + "]",
            _ => element.GetRawText(),
        };
    }

    private static int? CountJwksKeys(string? jwks)
    {
        if (string.IsNullOrWhiteSpace(jwks))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(jwks);
            return document.RootElement.TryGetProperty("keys", out var keys) && keys.ValueKind == JsonValueKind.Array
                ? keys.GetArrayLength()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
