using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace _42.Platform.Storyteller.KeyValues.ClientEncryption;

// A client-encrypted value: JWE General JSON Serialization (RFC 7516 section 7.2.1) with one entry per recipient.
// The server parses and validates it; only the holder of a recipient's private key reads it.
public sealed class JweDocument
{
    public const string Encryption = "A256GCM";
    public const string Algorithm = "RSA-OAEP-256";
    public const string PlaceHeader = "2s";
    public const string ContentTypeHeader = "cty";

    private JweDocument(JsonObject protectedHeader, string protectedBase64, IReadOnlyList<JweRecipient> recipients, byte[] iv, byte[] ciphertext, byte[] tag)
    {
        ProtectedHeader = protectedHeader;
        ProtectedBase64 = protectedBase64;
        Recipients = recipients;
        Iv = iv;
        Ciphertext = ciphertext;
        Tag = tag;
    }

    public JsonObject ProtectedHeader { get; }

    // The base64url protected header as received: the additional authenticated data of the content.
    public string ProtectedBase64 { get; }

    public IReadOnlyList<JweRecipient> Recipients { get; }

    public byte[] Iv { get; }

    public byte[] Ciphertext { get; }

    public byte[] Tag { get; }

    public string? Enc => ProtectedHeader["enc"]?.GetValue<string>();

    public string? Place => ProtectedHeader[PlaceHeader]?.GetValue<string>();

    public string? ContentType => ProtectedHeader[ContentTypeHeader]?.GetValue<string>();

    public static JweDocument Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        JsonNode? node;

        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new FormatException("The client-encrypted value is not JSON.", exception);
        }

        if (node is not JsonObject root)
        {
            throw new FormatException("The client-encrypted value is not a JSON object.");
        }

        var protectedBase64 = RequireString(root, "protected");
        JsonObject protectedHeader;

        try
        {
            protectedHeader = JsonNode.Parse(Encoding.UTF8.GetString(Base64Url.DecodeFromChars(protectedBase64))) as JsonObject
                ?? throw new FormatException("The protected header is not a JSON object.");
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        {
            throw new FormatException("The protected header of the client-encrypted value is malformed.", exception);
        }

        if (root["recipients"] is not JsonArray recipientsArray || recipientsArray.Count == 0)
        {
            throw new FormatException("The client-encrypted value has no recipients.");
        }

        var recipients = new List<JweRecipient>(recipientsArray.Count);

        foreach (var entry in recipientsArray)
        {
            if (entry is not JsonObject recipient || recipient["header"] is not JsonObject header)
            {
                throw new FormatException("A recipient of the client-encrypted value has no header.");
            }

            recipients.Add(new JweRecipient(
                RequireString(header, "kid"),
                RequireString(header, "alg"),
                Decode(RequireString(recipient, "encrypted_key"), "encrypted_key")));
        }

        return new JweDocument(
            protectedHeader,
            protectedBase64,
            recipients,
            Decode(RequireString(root, "iv"), "iv"),
            Decode(RequireString(root, "ciphertext"), "ciphertext"),
            Decode(RequireString(root, "tag"), "tag"));
    }

    public static string Serialize(JsonObject protectedHeader, IReadOnlyList<JweRecipient> recipients, byte[] iv, byte[] ciphertext, byte[] tag)
    {
        var protectedBase64 = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(protectedHeader.ToJsonString()));
        var root = new JsonObject
        {
            ["protected"] = protectedBase64,
            ["recipients"] = new JsonArray(recipients.Select(recipient => (JsonNode)new JsonObject
            {
                ["header"] = new JsonObject { ["alg"] = recipient.Algorithm, ["kid"] = recipient.KeyId },
                ["encrypted_key"] = Base64Url.EncodeToString(recipient.EncryptedKey),
            }).ToArray()),
            ["iv"] = Base64Url.EncodeToString(iv),
            ["ciphertext"] = Base64Url.EncodeToString(ciphertext),
            ["tag"] = Base64Url.EncodeToString(tag),
        };

        return root.ToJsonString();
    }

    private static string RequireString(JsonObject node, string name)
    {
        return node[name]?.GetValue<string>()
            ?? throw new FormatException($"The client-encrypted value has no '{name}'.");
    }

    private static byte[] Decode(string value, string name)
    {
        try
        {
            return Base64Url.DecodeFromChars(value);
        }
        catch (FormatException exception)
        {
            throw new FormatException($"The '{name}' of the client-encrypted value is not base64url.", exception);
        }
    }
}

public sealed record JweRecipient(string KeyId, string Algorithm, byte[] EncryptedKey);
