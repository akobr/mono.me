using System;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace _42.Platform.Storyteller.Entities.Access;

// The signing key of Storyteller tokens: core container, partition signing-keys, id tsk.1.
// Stored unprotected, like the PKCS#12 of the machine certificate authority.
public record class TokenSigningKeyEntity
{
    public string PartitionKey => "signing-keys";

    [JsonProperty("id")]
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    public required string Kid { get; init; }

    public required string Algorithm { get; init; }

    public required byte[] PrivateKeyPkcs8 { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
